using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Domain;

namespace WalletApi.Application.Credit;

public record Factor(string Name, int WeightPercent, double Value, double Points, string Reason);

public record CreditScore(int Score, string Band, List<Factor> Factors, DateTime ComputedAt);

/// <summary>
/// Transparent weighted scorecard, 300-850. Every factor is normalised to 0..1, weighted, and explained,
/// so a score can always be justified to the customer and the regulator. Thin files get neutral values, not zero.
/// </summary>
public class CreditScoreService(IWalletDb db)
{
    public const int Min = 300, Max = 850;

    public static string BandFor(int score) => score switch
    {
        >= 800 => "Exceptional",
        >= 740 => "Very Good",
        >= 670 => "Good",
        >= 580 => "Fair",
        _ => "Poor",
    };

    static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    public async Task<CreditScore> ComputeAsync(Guid userId, DateTime now)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var accounts = await db.Accounts.Where(a => a.UserId == userId).Select(a => new { a.Id, a.Balance }).ToListAsync();
        var ids = accounts.Select(a => a.Id).ToHashSet();
        var idList = ids.ToList();
        var since = now.AddDays(-180);
        var txs = (await db.Transfers
            .Where(t => t.CreatedAt >= since && ((t.FromAccountId != null && idList.Contains(t.FromAccountId.Value)) || idList.Contains(t.ToAccountId)))
            .Select(t => new { t.Type, t.Amount, t.CreatedAt, t.FromAccountId, t.ToAccountId }).ToListAsync())
            .Select(t => new
            {
                t.Type,
                t.Amount,
                t.CreatedAt,
                Incoming = ids.Contains(t.ToAccountId) && t.Type is TxType.Transfer or TxType.Deposit,
                Outgoing = t.FromAccountId is { } f && ids.Contains(f) && t.Type is TxType.Transfer or TxType.LoanRepayment,
            }).ToList();

        var factors = new List<Factor>();
        void Add(string name, int weight, double value, string reason) =>
            factors.Add(new Factor(name, weight, Math.Round(value, 3), Math.Round(weight / 100.0 * value * (Max - Min), 1), reason));

        // 1. Account age (10%)
        var ageDays = user is null ? 0 : (now - user.CreatedAt).TotalDays;
        Add("Account age", 10, Clamp01(ageDays / 365), $"{(int)ageDays} days old (full marks at 1 year)");

        // 2. Income regularity (20%): low variation of 30-day inflow buckets
        var months = Math.Clamp((int)(ageDays / 30), 0, 6);
        if (months < 2) Add("Income regularity", 20, 0.4, "Not enough history yet (neutral)");
        else
        {
            var buckets = Enumerable.Range(0, months).Select(i =>
                (double)txs.Where(t => t.Incoming && t.CreatedAt > now.AddDays(-30 * (i + 1)) && t.CreatedAt <= now.AddDays(-30 * i)).Sum(t => t.Amount)).ToList();
            var mean = buckets.Average();
            var sd = Math.Sqrt(buckets.Average(b => Math.Pow(b - mean, 2)));
            var cv = mean > 0 ? sd / mean : 2;
            Add("Income regularity", 20, Clamp01(1 - cv / 1.5), $"Monthly inflow variation {cv:0.00} (lower is steadier)");
        }

        // 3. Savings rate, last 90 days (15%): full marks at saving 30% of income
        var cutoff = now.AddDays(-90);
        var inflow = txs.Where(t => t.Incoming && t.CreatedAt >= cutoff).Sum(t => t.Amount);
        var outflow = txs.Where(t => t.Outgoing && t.CreatedAt >= cutoff).Sum(t => t.Amount);
        if (inflow == 0) Add("Savings rate", 15, 0.3, "No income in the last 90 days");
        else
        {
            var rate = (double)(inflow - outflow) / inflow;
            Add("Savings rate", 15, Clamp01(rate / 0.3), $"Kept {rate:P0} of income (full marks at 30%)");
        }

        // 4. Balance cushion (15%): current balance vs average monthly spending, full marks at 3 months
        var balance = accounts.Sum(a => a.Balance);
        var monthlyOut = outflow / 3.0;
        if (monthlyOut == 0) Add("Balance cushion", 15, balance > 0 ? 0.5 : 0, "No recent spending to compare against");
        else Add("Balance cushion", 15, Clamp01(balance / monthlyOut / 3), $"Balance covers {balance / monthlyOut:0.0} months of spending");

        // 5. Repayment history (30%): on-time installments (3-day grace); unpaid overdue ones count as missed
        var due = await db.LoanInstallments
            .Where(i => i.DueDate <= now && db.Loans.Any(l => l.Id == i.LoanId && l.UserId == userId))
            .Select(i => new { i.Status, i.DueDate, i.PaidAt }).ToListAsync();
        if (due.Count == 0) Add("Repayment history", 30, 0.5, "No loan history (neutral)");
        else
        {
            var onTime = due.Count(i => i.Status == InstallmentStatus.Paid && i.PaidAt <= i.DueDate.AddDays(3));
            Add("Repayment history", 30, (double)onTime / due.Count, $"{onTime} of {due.Count} installments paid on time");
        }

        // 6. Activity (10%): transactions in last 90 days, full marks at 20
        var count = txs.Count(t => t.CreatedAt >= cutoff && t.Type is TxType.Transfer or TxType.Deposit);
        Add("Activity", 10, Clamp01(count / 20.0), $"{count} transactions in the last 90 days");

        var score = (int)Math.Round(Min + factors.Sum(f => f.Points));
        score = Math.Clamp(score, Min, Max);
        return new CreditScore(score, BandFor(score), factors, now);
    }

    /// <summary>Stores at most one snapshot per user per UTC day (latest wins) for trend charts.</summary>
    public async Task RecordSnapshotAsync(Guid userId, int score, DateTime now)
    {
        var day = now.Date;
        var snap = await db.CreditScoreSnapshots.FirstOrDefaultAsync(s => s.UserId == userId && s.Day == day);
        if (snap is null) db.CreditScoreSnapshots.Add(new CreditScoreSnapshot { UserId = userId, Day = day, Score = score });
        else snap.Score = score;
        await db.SaveChangesAsync();
    }

    public Task<List<CreditScoreSnapshot>> HistoryAsync(Guid userId) =>
        db.CreditScoreSnapshots.Where(s => s.UserId == userId).OrderBy(s => s.Day).Take(365).ToListAsync();
}
