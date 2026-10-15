using WalletApi.Application.Credit;
using WalletApi.Application.Lending;
using WalletApi.Domain;

namespace WalletApi.Application.Tests;

public class LoanTests : IDisposable
{
    static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    readonly TestDb t = new();
    LoanService Svc => new(t.Db, new CreditScoreService(t.Db));

    public LoanTests()
    {
        t.Db.Accounts.Add(new Account { Id = Ids.LenderPool, UserId = Guid.Empty });
        t.Db.SaveChanges();
    }

    public void Dispose() => t.Dispose();

    /// <summary>Gives the user six months of steady income and savings so they qualify.</summary>
    async Task MakeCreditworthy()
    {
        t.Db.Users.Add(new User { Id = t.UserId, Email = "u@x.com", CreatedAt = Now.AddDays(-400) });
        t.A.Balance = 3_000_000;
        for (var m = 0; m < 6; m++)
        {
            var at = Now.AddDays(-30 * m - 5);
            t.Db.Transfers.Add(new Transfer { IdempotencyKey = $"d{m}", ToAccountId = t.A.Id, Amount = 1_000_000, Type = TxType.Deposit, CreatedAt = at });
            t.Db.Transfers.Add(new Transfer { IdempotencyKey = $"s{m}", FromAccountId = t.A.Id, ToAccountId = t.B.Id, Amount = 400_000, CreatedAt = at });
        }
        await t.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(1_000_000, 12.0, 12)]
    [InlineData(250_000, 24.0, 6)]
    [InlineData(100_000, 0.0, 3)]
    public void Schedule_amortises_to_principal_plus_interest(long principal, double rate, int months)
    {
        var plan = LoanService.Schedule(principal, (decimal)rate, months);
        Assert.Equal(months, plan.Count);
        var r = rate / 1200;
        var expectedTotal = r == 0 ? principal : principal * r * months / (1 - Math.Pow(1 + r, -months));
        Assert.InRange(plan.Sum(), expectedTotal - months, expectedTotal + months); // within rounding
    }

    [Fact]
    public async Task New_user_is_not_eligible()
    {
        var offer = await Svc.OfferAsync(t.UserId, Now);
        Assert.False(offer.Eligible);
        var (loan, error) = await Svc.ApplyAsync(t.UserId, t.A.Id, 10_000, 3, Now);
        Assert.Null(loan);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Account_younger_than_30_days_is_not_eligible()
    {
        await MakeCreditworthy();
        var user = t.Db.Users.Single(u => u.Id == t.UserId);
        user.CreatedAt = Now.AddDays(-5);
        await t.Db.SaveChangesAsync();
        var offer = await Svc.OfferAsync(t.UserId, Now);
        Assert.False(offer.Eligible);
        Assert.Contains("30 days", offer.Reason);
    }

    [Fact]
    public async Task Eligible_user_gets_funds_and_a_schedule()
    {
        await MakeCreditworthy();
        var offer = await Svc.OfferAsync(t.UserId, Now);
        Assert.True(offer.Eligible, offer.Reason);
        var before = (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance;
        var (loan, error) = await Svc.ApplyAsync(t.UserId, t.A.Id, 100_000, 4, Now);
        Assert.Null(error);
        Assert.Equal(4, loan!.Installments.Count);
        Assert.Equal(before + 100_000, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance);
        Assert.Equal(-100_000, (await t.Db.Accounts.FindAsync(Ids.LenderPool))!.Balance);
    }

    [Fact]
    public async Task Amount_above_limit_and_second_active_loan_are_rejected()
    {
        await MakeCreditworthy();
        var offer = await Svc.OfferAsync(t.UserId, Now);
        var (_, tooBig) = await Svc.ApplyAsync(t.UserId, t.A.Id, offer.Limit + 1, 6, Now);
        Assert.Contains("exceeds", tooBig);
        Assert.Null((await Svc.ApplyAsync(t.UserId, t.A.Id, 50_000, 3, Now)).Error);
        Assert.Equal("You already have an active loan", (await Svc.ApplyAsync(t.UserId, t.A.Id, 50_000, 3, Now)).Error);
    }

    [Fact]
    public async Task Autopay_collects_due_installments_and_repays_the_loan()
    {
        await MakeCreditworthy();
        var (loan, _) = await Svc.ApplyAsync(t.UserId, t.A.Id, 100_000, 2, Now);
        Assert.Equal(0, await Svc.RunDueAsync(Now)); // nothing due yet
        Assert.Equal(2, await Svc.RunDueAsync(Now.AddMonths(2).AddDays(1)));
        var stored = (await Svc.ListAsync(t.UserId)).Single(l => l.Id == loan!.Id);
        Assert.Equal(LoanStatus.Repaid, stored.Status);
        Assert.All(stored.Installments, i => Assert.Equal(InstallmentStatus.Paid, i.Status));
    }

    [Fact]
    public async Task Unpaid_for_30_days_defaults_the_loan()
    {
        await MakeCreditworthy();
        var (loan, _) = await Svc.ApplyAsync(t.UserId, t.A.Id, 100_000, 2, Now);
        t.A.Balance = 0; // can't pay
        await t.Db.SaveChangesAsync();
        await Svc.RunDueAsync(Now.AddMonths(1).AddDays(31));
        Assert.Equal(LoanStatus.Defaulted, (await Svc.ListAsync(t.UserId)).Single(l => l.Id == loan!.Id).Status);
    }

    [Fact]
    public async Task Manual_repay_pays_next_installment_or_reports_insufficient_funds()
    {
        await MakeCreditworthy();
        var (loan, _) = await Svc.ApplyAsync(t.UserId, t.A.Id, 100_000, 2, Now);
        Assert.True((await Svc.RepayNextAsync(t.UserId, loan!.Id, Now)).Paid);
        t.A.Balance = 0;
        await t.Db.SaveChangesAsync();
        Assert.Equal("Insufficient funds", (await Svc.RepayNextAsync(t.UserId, loan.Id, Now)).Error);
    }
}
