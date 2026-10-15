using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Application.Credit;
using WalletApi.Domain;

namespace WalletApi.Application.Lending;

public record LoanOffer(bool Eligible, int Score, string Band, long Limit, decimal AnnualRatePercent, string? Reason);

public class LoanService(IWalletDb db, CreditScoreService scores)
{
    public const int MaxTermMonths = 36;
    public const int MinAccountAgeDays = 30;
    static readonly TimeSpan DefaultAfter = TimeSpan.FromDays(30);

    public static (long Limit, decimal Rate) Terms(int score) => score switch
    {
        >= 800 => (10_000_000, 10m),
        >= 740 => (5_000_000, 14m),
        >= 670 => (2_000_000, 18m),
        >= 580 => (500_000, 24m),
        _ => (0, 0m),
    };

    public async Task<LoanOffer> OfferAsync(Guid userId, DateTime now)
    {
        var s = await scores.ComputeAsync(userId, now);
        var createdAt = await db.Users.Where(u => u.Id == userId).Select(u => (DateTime?)u.CreatedAt).FirstOrDefaultAsync();
        if (createdAt is null || (now - createdAt.Value).TotalDays < MinAccountAgeDays)
            return new LoanOffer(false, s.Score, s.Band, 0, 0, $"Account must be at least {MinAccountAgeDays} days old");
        var (limit, rate) = Terms(s.Score);
        return limit == 0
            ? new LoanOffer(false, s.Score, s.Band, 0, 0, "Score too low (580 needed); build history with regular deposits and transfers")
            : new LoanOffer(true, s.Score, s.Band, limit, rate, null);
    }

    /// <summary>Equal installments; the last one absorbs rounding so the schedule fully amortises.</summary>
    public static List<long> Schedule(long principal, decimal annualPercent, int months)
    {
        var r = annualPercent / 1200m;
        var pay = r == 0 ? (decimal)principal / months : principal * r / (1 - (decimal)Math.Pow(1 + (double)r, -months));
        var result = new List<long>();
        var balance = (decimal)principal;
        for (var i = 1; i <= months; i++)
        {
            var interest = balance * r;
            if (i == months) { result.Add((long)Math.Round(balance + interest)); break; }
            var amount = Math.Round(pay);
            result.Add((long)amount);
            balance = balance + interest - amount;
        }
        return result;
    }

    public async Task<(Loan? Loan, string? Error)> ApplyAsync(Guid userId, Guid accountId, long amount, int termMonths, DateTime now)
    {
        if (amount <= 0) return (null, "Amount must be positive");
        if (termMonths is < 1 or > MaxTermMonths) return (null, $"Term must be 1-{MaxTermMonths} months");
        var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);
        if (acc is null) return (null, "Account not found");
        if (await db.Loans.AnyAsync(l => l.UserId == userId && l.Status == LoanStatus.Active)) return (null, "You already have an active loan");
        var pool = await db.Accounts.FirstOrDefaultAsync(a => a.Id == Ids.LenderPool);
        if (pool is null) return (null, "Lending is not available");

        var offer = await OfferAsync(userId, now);
        if (!offer.Eligible) return (null, offer.Reason);
        if (amount > offer.Limit) return (null, $"Amount exceeds your limit of {offer.Limit}");

        var loan = new Loan { UserId = userId, AccountId = accountId, Principal = amount, AnnualRatePercent = offer.AnnualRatePercent, TermMonths = termMonths, CreatedAt = now };
        var plan = Schedule(amount, offer.AnnualRatePercent, termMonths);
        for (var i = 0; i < plan.Count; i++)
            loan.Installments.Add(new LoanInstallment { LoanId = loan.Id, Number = i + 1, DueDate = now.AddMonths(i + 1), Amount = plan[i] });

        pool.Balance -= amount;
        acc.Balance += amount;
        db.Loans.Add(loan);
        db.Transfers.Add(new Transfer { IdempotencyKey = $"loan-{loan.Id}-disb", FromAccountId = pool.Id, ToAccountId = accountId, Amount = amount, Type = TxType.LoanDisbursement, CreatedAt = now });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return (null, "Concurrent update, retry"); }
        return (loan, null);
    }

    public Task<List<Loan>> ListAsync(Guid userId) =>
        db.Loans.Include(l => l.Installments).Where(l => l.UserId == userId).OrderByDescending(l => l.CreatedAt).ToListAsync();

    /// <summary>Pays the earliest pending installment now. Returns an error if funds are short.</summary>
    public async Task<(bool Paid, string? Error)> RepayNextAsync(Guid userId, Guid loanId, DateTime now)
    {
        var loan = await db.Loans.Include(l => l.Installments).FirstOrDefaultAsync(l => l.Id == loanId && l.UserId == userId);
        if (loan is null) return (false, "Loan not found");
        var next = loan.Installments.Where(i => i.Status == InstallmentStatus.Pending).OrderBy(i => i.Number).FirstOrDefault();
        if (next is null) return (false, "Nothing left to repay");
        return await PayAsync(loan, next, now) ? (true, null) : (false, "Insufficient funds");
    }

    async Task<bool> PayAsync(Loan loan, LoanInstallment inst, DateTime now)
    {
        var acc = await db.Accounts.FirstAsync(a => a.Id == loan.AccountId);
        var pool = await db.Accounts.FirstAsync(a => a.Id == Ids.LenderPool);
        if (acc.Balance < inst.Amount) return false;
        acc.Balance -= inst.Amount;
        pool.Balance += inst.Amount;
        inst.Status = InstallmentStatus.Paid;
        inst.PaidAt = now;
        if (loan.Installments.All(i => i.Status == InstallmentStatus.Paid)) loan.Status = LoanStatus.Repaid;
        db.Transfers.Add(new Transfer { IdempotencyKey = $"repay-{inst.Id}", FromAccountId = acc.Id, ToAccountId = pool.Id, Amount = inst.Amount, Type = TxType.LoanRepayment, CreatedAt = now });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; } // concurrent change or duplicate key: next tick retries
        return true;
    }

    /// <summary>Autopay: pays every due installment it can; installments unpaid 30+ days past due default the loan.</summary>
    public async Task<int> RunDueAsync(DateTime now)
    {
        var loanIds = await db.Loans.Where(l => l.Status == LoanStatus.Active && l.Installments.Any(i => i.Status == InstallmentStatus.Pending && i.DueDate <= now))
            .Select(l => l.Id).ToListAsync();
        var paid = 0;
        foreach (var id in loanIds)
        {
            var loan = await db.Loans.Include(l => l.Installments).FirstAsync(l => l.Id == id);
            foreach (var inst in loan.Installments.Where(i => i.Status == InstallmentStatus.Pending && i.DueDate <= now).OrderBy(i => i.Number).ToList())
            {
                if (await PayAsync(loan, inst, now)) { paid++; continue; }
                if (now - inst.DueDate > DefaultAfter && loan.Status == LoanStatus.Active)
                {
                    loan.Status = LoanStatus.Defaulted;
                    await db.SaveChangesAsync();
                }
                break; // keep installment order
            }
        }
        return paid;
    }
}
