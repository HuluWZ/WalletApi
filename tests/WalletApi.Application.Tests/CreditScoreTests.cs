using WalletApi.Application.Credit;
using WalletApi.Domain;

namespace WalletApi.Application.Tests;

public class CreditScoreTests : IDisposable
{
    static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    readonly TestDb t = new();
    CreditScoreService Svc => new(t.Db);

    public void Dispose() => t.Dispose();

    async Task Setup(int ageDays, Func<int, long> monthlyIncome, Func<int, long> monthlySpend, long balance)
    {
        t.Db.Users.Add(new User { Id = t.UserId, Email = "u@x.com", CreatedAt = Now.AddDays(-ageDays) });
        t.A.Balance = balance;
        for (var m = 0; m < ageDays / 30; m++)
        {
            var at = Now.AddDays(-30 * m - 5);
            if (monthlyIncome(m) > 0) t.Db.Transfers.Add(new Transfer { IdempotencyKey = $"d{m}", ToAccountId = t.A.Id, Amount = monthlyIncome(m), Type = TxType.Deposit, CreatedAt = at });
            if (monthlySpend(m) > 0) t.Db.Transfers.Add(new Transfer { IdempotencyKey = $"s{m}", FromAccountId = t.A.Id, ToAccountId = t.B.Id, Amount = monthlySpend(m), CreatedAt = at });
        }
        await t.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task New_user_gets_a_low_but_in_range_score()
    {
        await Setup(0, _ => 0, _ => 0, 0);
        var s = await Svc.ComputeAsync(t.UserId, Now);
        Assert.InRange(s.Score, CreditScoreService.Min, CreditScoreService.Max);
        Assert.Equal("Poor", s.Band);
    }

    [Fact]
    public async Task Steady_saver_scores_higher_than_erratic_spender()
    {
        await Setup(200, _ => 1_000_000, _ => 600_000, 3_000_000);
        var saver = await Svc.ComputeAsync(t.UserId, Now);

        using var t2 = new TestDb();
        t2.Db.Users.Add(new User { Id = t2.UserId, Email = "e@x.com", CreatedAt = Now.AddDays(-200) });
        for (var m = 0; m < 6; m++)
        {
            var at = Now.AddDays(-30 * m - 5);
            t2.Db.Transfers.Add(new Transfer { IdempotencyKey = $"d{m}", ToAccountId = t2.A.Id, Amount = m % 2 == 0 ? 2_000_000 : 50_000, Type = TxType.Deposit, CreatedAt = at });
            t2.Db.Transfers.Add(new Transfer { IdempotencyKey = $"s{m}", FromAccountId = t2.A.Id, ToAccountId = t2.B.Id, Amount = m % 2 == 0 ? 1_900_000 : 50_000, CreatedAt = at });
        }
        t2.A.Balance = 0;
        await t2.Db.SaveChangesAsync();
        var erratic = await new CreditScoreService(t2.Db).ComputeAsync(t2.UserId, Now);

        Assert.True(saver.Score > erratic.Score, $"{saver.Score} should beat {erratic.Score}");
    }

    [Fact]
    public async Task Factors_are_explained_and_weights_sum_to_100()
    {
        await Setup(100, _ => 500_000, _ => 300_000, 800_000);
        var s = await Svc.ComputeAsync(t.UserId, Now);
        Assert.Equal(100, s.Factors.Sum(f => f.WeightPercent));
        Assert.All(s.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
    }

    [Fact]
    public async Task Missed_installments_lower_the_score()
    {
        await Setup(200, _ => 1_000_000, _ => 600_000, 3_000_000);
        var before = await Svc.ComputeAsync(t.UserId, Now);
        var loan = new Loan { UserId = t.UserId, AccountId = t.A.Id, Principal = 100_000, TermMonths = 2, CreatedAt = Now.AddDays(-90) };
        loan.Installments.Add(new LoanInstallment { LoanId = loan.Id, Number = 1, DueDate = Now.AddDays(-60), Amount = 50_000 });
        loan.Installments.Add(new LoanInstallment { LoanId = loan.Id, Number = 2, DueDate = Now.AddDays(-30), Amount = 50_000 });
        t.Db.Loans.Add(loan);
        await t.Db.SaveChangesAsync();
        var after = await Svc.ComputeAsync(t.UserId, Now);
        Assert.True(after.Score < before.Score);
    }

    [Fact]
    public async Task Snapshot_is_one_per_day_latest_wins()
    {
        await Svc.RecordSnapshotAsync(t.UserId, 600, Now);
        await Svc.RecordSnapshotAsync(t.UserId, 650, Now.AddHours(3));
        var h = Assert.Single(await Svc.HistoryAsync(t.UserId));
        Assert.Equal(650, h.Score);
    }

    [Theory]
    [InlineData(300, "Poor")]
    [InlineData(580, "Fair")]
    [InlineData(670, "Good")]
    [InlineData(740, "Very Good")]
    [InlineData(850, "Exceptional")]
    public void Bands(int score, string band) => Assert.Equal(band, CreditScoreService.BandFor(score));
}
