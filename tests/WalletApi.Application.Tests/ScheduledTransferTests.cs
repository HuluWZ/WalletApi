using WalletApi.Application.Scheduling;
using WalletApi.Application.Transfers;
using WalletApi.Domain;

namespace WalletApi.Application.Tests;

public class ScheduledTransferTests : IDisposable
{
    static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    readonly TestDb t = new();
    ScheduledTransferService Svc => new(t.Db, t.Transfers);

    public void Dispose() => t.Dispose();

    async Task<ScheduledTransfer> Create(long amount, Interval i, DateTime nextRun)
    {
        var s = new ScheduledTransfer { UserId = t.UserId, FromAccountId = t.A.Id, ToAccountId = t.B.Id, Amount = amount, Interval = i, NextRunAt = nextRun };
        t.Db.ScheduledTransfers.Add(s);
        await t.Db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Due_schedule_runs_once_and_advances()
    {
        var s = await Create(100, Interval.Weekly, Now.AddMinutes(-1));
        Assert.Equal(1, await Svc.RunDueAsync(Now));
        Assert.Equal(0, await Svc.RunDueAsync(Now)); // already advanced, nothing due
        Assert.Equal(900, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance);
        Assert.Equal(Now.AddMinutes(-1).AddDays(7), (await t.Db.ScheduledTransfers.FindAsync(s.Id))!.NextRunAt);
    }

    [Fact]
    public async Task Missed_runs_are_skipped_not_backfilled()
    {
        var s = await Create(100, Interval.Daily, Now.AddDays(-30));
        await Svc.RunDueAsync(Now);
        Assert.Equal(900, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance); // one payment, not 30
        Assert.True((await t.Db.ScheduledTransfers.FindAsync(s.Id))!.NextRunAt > Now);
    }

    [Fact]
    public async Task Past_first_run_is_rejected()
    {
        var (item, error) = await Svc.CreateAsync(t.UserId, t.A.Id, t.B.Id, 10, Interval.Daily, DateTime.UtcNow.AddDays(-1));
        Assert.Null(item);
        Assert.Equal("First run must be in the future", error);
    }

    [Fact]
    public async Task Future_schedule_is_not_run()
    {
        await Create(100, Interval.Daily, Now.AddHours(1));
        Assert.Equal(0, await Svc.RunDueAsync(Now));
        Assert.Equal(1000, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance);
    }

    [Fact]
    public async Task Failed_run_records_error_and_is_skipped()
    {
        var s = await Create(5000, Interval.Daily, Now.AddMinutes(-1));
        await Svc.RunDueAsync(Now);
        var after = (await t.Db.ScheduledTransfers.FindAsync(s.Id))!;
        Assert.Equal("Insufficient funds", after.LastError);
        Assert.True(after.NextRunAt > Now);
    }

    [Fact]
    public async Task Cancelled_schedule_does_not_run()
    {
        var s = await Create(100, Interval.Daily, Now.AddMinutes(-1));
        Assert.True(await Svc.CancelAsync(t.UserId, s.Id));
        Assert.Equal(0, await Svc.RunDueAsync(Now));
    }

    [Fact]
    public async Task Cannot_schedule_from_someone_elses_account()
    {
        var (item, error) = await Svc.CreateAsync(Guid.NewGuid(), t.A.Id, t.B.Id, 10, Interval.Daily, DateTime.UtcNow.AddDays(1));
        Assert.Null(item);
        Assert.Equal("Account not found", error);
    }

    [Theory]
    [InlineData(Interval.Daily, 1)]
    [InlineData(Interval.Weekly, 7)]
    public void Advance_moves_by_interval(Interval i, int days)
    {
        var s = new ScheduledTransfer { Interval = i, NextRunAt = Now };
        s.Advance();
        Assert.Equal(Now.AddDays(days), s.NextRunAt);
    }

    [Fact]
    public void Monthly_advance_handles_short_months()
    {
        var s = new ScheduledTransfer { Interval = Interval.Monthly, NextRunAt = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc) };
        s.Advance();
        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), s.NextRunAt);
    }
}
