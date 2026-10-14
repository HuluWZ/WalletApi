using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Application.Transfers;
using WalletApi.Domain;

namespace WalletApi.Application.Scheduling;

public class ScheduledTransferService(IWalletDb db, TransferService transfers)
{
    public async Task<(ScheduledTransfer? Item, string? Error)> CreateAsync(
        Guid userId, Guid from, Guid to, long amount, Interval interval, DateTime firstRunAt)
    {
        if (amount <= 0) return (null, "Amount must be positive");
        if (from == to) return (null, "Cannot transfer to the same account");
        if (firstRunAt.ToUniversalTime() < DateTime.UtcNow.AddMinutes(-1)) return (null, "First run must be in the future");
        if (!await db.Accounts.AnyAsync(a => a.Id == from && a.UserId == userId)) return (null, "Account not found");
        if (!await db.Accounts.AnyAsync(a => a.Id == to)) return (null, "Account not found");

        var s = new ScheduledTransfer
        {
            UserId = userId,
            FromAccountId = from,
            ToAccountId = to,
            Amount = amount,
            Interval = interval,
            NextRunAt = firstRunAt.ToUniversalTime(),
        };
        db.ScheduledTransfers.Add(s);
        await db.SaveChangesAsync();
        return (s, null);
    }

    public Task<List<ScheduledTransfer>> ListAsync(Guid userId) =>
        db.ScheduledTransfers.Where(s => s.UserId == userId).OrderBy(s => s.NextRunAt).ToListAsync();

    public async Task<bool> CancelAsync(Guid userId, Guid id)
    {
        var s = await db.ScheduledTransfers.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (s is null) return false;
        s.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Executes every due schedule once. The per-run idempotency key makes crashes/retries safe.</summary>
    public async Task<int> RunDueAsync(DateTime now)
    {
        var due = await db.ScheduledTransfers
            .Where(s => s.IsActive && s.NextRunAt <= now)
            .Select(s => s.Id).ToListAsync();

        foreach (var id in due)
        {
            var s = await db.ScheduledTransfers.FirstAsync(x => x.Id == id);
            var key = $"sched-{s.Id}-{s.NextRunAt:O}";
            var res = await transfers.TransferAsync(s.UserId, s.FromAccountId, s.ToAccountId, s.Amount, key);
            if (res.Error == TransferService.ConcurrentError) continue; // retry on the next tick

            s = await db.ScheduledTransfers.FirstAsync(x => x.Id == id); // may be detached after a tracker clear
            s.LastError = res.Error;
            do s.Advance(); while (s.NextRunAt <= now); // failed or missed (downtime) runs are skipped, never backfilled
            await db.SaveChangesAsync();
        }
        return due.Count;
    }
}
