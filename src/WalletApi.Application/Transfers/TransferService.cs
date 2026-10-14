using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Domain;

namespace WalletApi.Application.Transfers;

public record TransferResult(Transfer? Transfer, string? Error, bool Replayed = false);

public class TransferService(IWalletDb db)
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

    public Task<long> TotalBalanceAsync() => db.Accounts.SumAsync(a => a.Balance);
}
