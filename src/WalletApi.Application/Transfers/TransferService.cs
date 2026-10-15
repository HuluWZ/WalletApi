using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Application.Compliance;
using WalletApi.Domain;

namespace WalletApi.Application.Transfers;

public record TransferResult(Transfer? Transfer, string? Error, bool Replayed = false);

public class TransferService(IWalletDb db, ComplianceService compliance)
{
    public const string ConcurrentError = "Concurrent update, retry";

    public async Task<TransferResult> TransferAsync(Guid userId, Guid from, Guid to, long amount, string key)
    {
        if (amount <= 0) return new(null, "Amount must be positive");
        if (from == to) return new(null, "Cannot transfer to the same account");

        var existing = await db.Transfers.FirstOrDefaultAsync(t => t.IdempotencyKey == key);
        if (existing is not null) return new(existing, null, Replayed: true);

        var src = await db.Accounts.FirstOrDefaultAsync(a => a.Id == from && a.UserId == userId);
        var dst = await db.Accounts.FirstOrDefaultAsync(a => a.Id == to);
        if (src is null || dst is null) return new(null, "Account not found");
        if (src.Balance < amount) return new(null, "Insufficient funds");

        src.Balance -= amount;
        dst.Balance += amount;
        var t = new Transfer { IdempotencyKey = key, FromAccountId = from, ToAccountId = to, Amount = amount };
        db.Transfers.Add(t);
        await compliance.EvaluateAsync(t); // flags commit atomically with the transfer
        try
        {
            await db.SaveChangesAsync(); // one atomic unit; Version check rejects concurrent edits
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(null, ConcurrentError);
        }
        catch (DbUpdateException)
        {
            // lost an idempotency-key race: return the winner's result
            db.ChangeTracker.Clear();
            var winner = await db.Transfers.FirstOrDefaultAsync(x => x.IdempotencyKey == key);
            if (winner is not null) return new(winner, null, Replayed: true);
            throw;
        }
        return new(t, null);
    }

    public async Task<(Account? Account, string? Error)> DepositAsync(Guid userId, Guid accountId, long amount)
    {
        if (amount <= 0) return (null, "Amount must be positive");
        var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);
        if (acc is null) return (null, "Account not found");
        acc.Balance += amount;
        var t = new Transfer { IdempotencyKey = $"dep-{Guid.NewGuid()}", ToAccountId = accountId, Amount = amount, Type = TxType.Deposit };
        db.Transfers.Add(t);
        await compliance.EvaluateAsync(t);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return (null, ConcurrentError);
        }
        return (acc, null);
    }

    public Task<long> TotalBalanceAsync() => db.Accounts.SumAsync(a => a.Balance);
}
