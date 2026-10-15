using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Domain;

namespace WalletApi.Application.Compliance;

/// <summary>Amounts are minor units. Thresholds are meant to be set per regulation via the "Compliance" config section.</summary>
public class ComplianceOptions
{
    public long SingleThreshold { get; set; } = 1_000_000;
    public long DailyThreshold { get; set; } = 3_000_000;
    public int StructuringBandPercent { get; set; } = 90;
    public int StructuringCount { get; set; } = 3;
    public int PassThroughPercent { get; set; } = 80;
}

public record FlagReview(FlagStatus Status, string? Note);

public class ComplianceService(IWalletDb db, ComplianceOptions o)
{
    /// <summary>Adds flags for a not-yet-saved transaction to the context, so flags commit atomically with the money movement.</summary>
    public async Task EvaluateAsync(Transfer t)
    {
        if (t.Type is not (TxType.Transfer or TxType.Deposit)) return;
        var flags = new List<ComplianceFlag>();
        void Add(Guid account, FlagRule rule, string details) =>
            flags.Add(new ComplianceFlag { TransferId = t.Id, AccountId = account, Rule = rule, Details = details, CreatedAt = t.CreatedAt });

        if (t.Amount >= o.SingleThreshold)
            Add(t.FromAccountId ?? t.ToAccountId, FlagRule.LargeTransaction, $"Amount {t.Amount} >= threshold {o.SingleThreshold}");

        if (t.FromAccountId is { } from)
        {
            var day = t.CreatedAt.AddHours(-24);
            var hour = t.CreatedAt.AddHours(-1);
            var sent = await db.Transfers
                .Where(x => x.FromAccountId == from && x.Type == TxType.Transfer && x.CreatedAt >= day && x.CreatedAt <= t.CreatedAt)
                .Select(x => new { x.Amount, x.CreatedAt }).ToListAsync();
            sent.Add(new { t.Amount, t.CreatedAt }); // t is not saved yet

            var total = sent.Sum(x => x.Amount);
            if (total >= o.DailyThreshold && total - t.Amount < o.DailyThreshold)
                Add(from, FlagRule.DailyTotal, $"24h outgoing total {total} >= {o.DailyThreshold}");

            var low = o.SingleThreshold * o.StructuringBandPercent / 100;
            var nearCount = sent.Count(x => x.Amount >= low && x.Amount < o.SingleThreshold);
            if (t.Amount >= low && t.Amount < o.SingleThreshold && nearCount >= o.StructuringCount)
                Add(from, FlagRule.Structuring, $"{nearCount} transfers in 24h between {low} and {o.SingleThreshold} (just under threshold)");

            var incoming = (await db.Transfers
                .Where(x => x.ToAccountId == from && (x.Type == TxType.Transfer || x.Type == TxType.Deposit) && x.CreatedAt >= hour && x.CreatedAt <= t.CreatedAt)
                .Select(x => x.Amount).ToListAsync()).Sum();
            var outgoing = sent.Where(x => x.CreatedAt >= hour).Sum(x => x.Amount);
            var limit = incoming * o.PassThroughPercent / 100;
            if (incoming >= o.SingleThreshold / 2 && outgoing >= limit && outgoing - t.Amount < limit)
                Add(from, FlagRule.RapidPassThrough, $"Sent {outgoing} within 1h of receiving {incoming} (>= {o.PassThroughPercent}%)");
        }

        db.ComplianceFlags.AddRange(flags);
    }

    public Task<List<ComplianceFlag>> ListAsync(FlagStatus? status) =>
        db.ComplianceFlags.Where(f => status == null || f.Status == status).OrderByDescending(f => f.CreatedAt).Take(500).ToListAsync();

    public async Task<ComplianceFlag?> ReviewAsync(Guid flagId, Guid reviewerId, FlagReview r, DateTime now)
    {
        var f = await db.ComplianceFlags.FirstOrDefaultAsync(x => x.Id == flagId);
        if (f is null) return null;
        f.Status = r.Status;
        f.ReviewNote = r.Note;
        f.ReviewedBy = reviewerId;
        f.ReviewedAt = now;
        await db.SaveChangesAsync();
        return f;
    }

    public record DayStat(DateTime Day, long Volume, int Count, int Flags);

    // ponytail: grouped in memory; push to SQL / materialized rollups if volume grows
    public async Task<List<DayStat>> StatsAsync(int days, DateTime now)
    {
        var since = now.Date.AddDays(1 - days);
        var txs = await db.Transfers.Where(t => t.CreatedAt >= since && (t.Type == TxType.Transfer || t.Type == TxType.Deposit))
            .Select(t => new { t.CreatedAt, t.Amount }).ToListAsync();
        var flags = await db.ComplianceFlags.Where(f => f.CreatedAt >= since).Select(f => f.CreatedAt).ToListAsync();
        return Enumerable.Range(0, days).Select(i => since.AddDays(i)).Select(d => new DayStat(
            d,
            txs.Where(t => t.CreatedAt.Date == d).Sum(t => t.Amount),
            txs.Count(t => t.CreatedAt.Date == d),
            flags.Count(f => f.Date == d))).ToList();
    }

    public record Node(Guid Id, string Label, bool Flagged, long Volume);
    public record Edge(Guid From, Guid To, long Total, int Count, bool Flagged);
    public record Network(List<Node> Nodes, List<Edge> Edges);

    /// <summary>Account-to-account transfer graph for the window; flagged accounts/edges are those with an open flag.</summary>
    public async Task<Network> NetworkAsync(int days, DateTime now)
    {
        var since = now.AddDays(-days);
        var txs = await db.Transfers.Where(t => t.CreatedAt >= since && t.Type == TxType.Transfer && t.FromAccountId != null)
            .Select(t => new { t.Id, From = t.FromAccountId!.Value, To = t.ToAccountId, t.Amount }).ToListAsync();
        var open = await db.ComplianceFlags.Where(f => f.Status == FlagStatus.Open || f.Status == FlagStatus.Escalated)
            .Select(f => new { f.TransferId, f.AccountId }).ToListAsync();
        var flaggedTx = open.Select(f => f.TransferId).ToHashSet();
        var flaggedAcc = open.Select(f => f.AccountId).ToHashSet();

        var edges = txs.GroupBy(t => (t.From, t.To))
            .Select(g => new Edge(g.Key.From, g.Key.To, g.Sum(x => x.Amount), g.Count(), g.Any(x => flaggedTx.Contains(x.Id)))).ToList();
        var nodes = edges.SelectMany(e => new[] { e.From, e.To }).Distinct().Select(id => new Node(
            id, id.ToString("N")[..8], flaggedAcc.Contains(id),
            edges.Where(e => e.From == id || e.To == id).Sum(e => e.Total))).ToList();
        return new Network(nodes, edges);
    }
}
