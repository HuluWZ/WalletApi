using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Compliance;
using WalletApi.Domain;

namespace WalletApi.Application.Tests;

public class ComplianceTests : IDisposable
{
    static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    readonly TestDb t = new();
    readonly ComplianceOptions o = new(); // single 1,000,000; daily 3,000,000; band 90%; count 3
    ComplianceService Svc => new(t.Db, o);

    public void Dispose() => t.Dispose();

    Transfer Tx(long amount, DateTime at, Guid? from = null, Guid? to = null, TxType type = TxType.Transfer) =>
        new() { IdempotencyKey = Guid.NewGuid().ToString(), FromAccountId = type == TxType.Deposit ? null : from ?? t.A.Id, ToAccountId = to ?? t.B.Id, Amount = amount, Type = type, CreatedAt = at };

    async Task<List<ComplianceFlag>> Run(Transfer tx)
    {
        t.Db.Transfers.Add(tx);
        await Svc.EvaluateAsync(tx);
        await t.Db.SaveChangesAsync();
        return await t.Db.ComplianceFlags.Where(f => f.TransferId == tx.Id).ToListAsync();
    }

    [Fact]
    public async Task Small_transfer_is_not_flagged() => Assert.Empty(await Run(Tx(10_000, Now)));

    [Fact]
    public async Task Transfer_at_threshold_is_flagged_large()
    {
        var f = Assert.Single(await Run(Tx(1_000_000, Now)));
        Assert.Equal(FlagRule.LargeTransaction, f.Rule);
        Assert.Equal(FlagStatus.Open, f.Status);
    }

    [Fact]
    public async Task Large_cash_deposit_is_flagged()
    {
        var f = Assert.Single(await Run(Tx(2_000_000, Now, type: TxType.Deposit)));
        Assert.Equal(FlagRule.LargeTransaction, f.Rule);
        Assert.Equal(t.B.Id, f.AccountId);
    }

    [Fact]
    public async Task Daily_total_flags_once_when_crossed()
    {
        for (var i = 0; i < 3; i++) Assert.Empty(await Run(Tx(800_000, Now.AddMinutes(i)))); // 2.4M so far
        var crossing = await Run(Tx(800_000, Now.AddMinutes(3))); // -> 3.2M
        Assert.Contains(crossing, f => f.Rule == FlagRule.DailyTotal);
        Assert.DoesNotContain(await Run(Tx(10_000, Now.AddMinutes(4))), f => f.Rule == FlagRule.DailyTotal);
    }

    [Fact]
    public async Task Structuring_flags_repeated_just_under_threshold_transfers()
    {
        Assert.Empty(await Run(Tx(950_000, Now)));
        Assert.Empty(await Run(Tx(960_000, Now.AddHours(1))));
        var third = await Run(Tx(970_000, Now.AddHours(2)));
        Assert.Contains(third, f => f.Rule == FlagRule.Structuring);
    }

    [Fact]
    public async Task Rapid_pass_through_is_flagged()
    {
        t.Db.Transfers.Add(Tx(900_000, Now.AddMinutes(-30), to: t.A.Id, type: TxType.Deposit)); // money in
        await t.Db.SaveChangesAsync();
        var flags = await Run(Tx(800_000, Now)); // 800k of 900k back out within the hour
        Assert.Contains(flags, f => f.Rule == FlagRule.RapidPassThrough);
    }

    [Fact]
    public async Task Loan_movements_are_never_flagged()
    {
        var tx = Tx(5_000_000, Now, type: TxType.LoanDisbursement);
        Assert.Empty(await Run(tx));
    }

    [Fact]
    public async Task Review_records_reviewer_and_status()
    {
        var flag = Assert.Single(await Run(Tx(1_000_000, Now)));
        var reviewer = Guid.NewGuid();
        var reviewed = await Svc.ReviewAsync(flag.Id, reviewer, new FlagReview(FlagStatus.Escalated, "refer"), Now);
        Assert.Equal(FlagStatus.Escalated, reviewed!.Status);
        Assert.Equal(reviewer, reviewed.ReviewedBy);
    }

    [Fact]
    public async Task Network_marks_flagged_edges_and_accounts()
    {
        await Run(Tx(1_000_000, Now));
        await Run(Tx(5_000, Now.AddMinutes(1)));
        var n = await Svc.NetworkAsync(30, Now.AddHours(1));
        var edge = Assert.Single(n.Edges);
        Assert.True(edge.Flagged);
        Assert.Equal(2, edge.Count);
        Assert.Contains(n.Nodes, x => x.Id == t.A.Id && x.Flagged);
    }

    [Fact]
    public async Task Stats_has_one_row_per_day_with_flag_counts()
    {
        await Run(Tx(1_000_000, Now));
        var stats = await Svc.StatsAsync(7, Now);
        Assert.Equal(7, stats.Count);
        Assert.Equal(1, stats[^1].Flags);
        Assert.Equal(1_000_000, stats[^1].Volume);
    }
}
