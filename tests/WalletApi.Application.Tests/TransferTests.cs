using WalletApi.Application.Transfers;

namespace WalletApi.Application.Tests;

public class TransferTests : IDisposable
{
    readonly TestDb t = new();
    TransferService Svc => new(t.Db);

    public void Dispose() => t.Dispose();

    [Fact]
    public async Task Transfer_moves_money_and_conserves_total()
    {
        var r = await Svc.TransferAsync(t.UserId, t.A.Id, t.B.Id, 300, "k1");
        Assert.Null(r.Error);
        Assert.Equal(700, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance);
        Assert.Equal(300, (await t.Db.Accounts.FindAsync(t.B.Id))!.Balance);
        Assert.Equal(1000, await Svc.TotalBalanceAsync());
    }

    [Fact]
    public async Task Same_idempotency_key_is_applied_once()
    {
        await Svc.TransferAsync(t.UserId, t.A.Id, t.B.Id, 300, "k1");
        var again = await Svc.TransferAsync(t.UserId, t.A.Id, t.B.Id, 300, "k1");
        Assert.True(again.Replayed);
        Assert.Equal(700, (await t.Db.Accounts.FindAsync(t.A.Id))!.Balance);
    }

    [Fact]
    public async Task Insufficient_funds_is_rejected()
    {
        var r = await Svc.TransferAsync(t.UserId, t.A.Id, t.B.Id, 5000, "k2");
        Assert.Equal("Insufficient funds", r.Error);
    }

    [Fact]
    public async Task Cannot_spend_from_someone_elses_account()
    {
        var r = await Svc.TransferAsync(Guid.NewGuid(), t.A.Id, t.B.Id, 10, "k3");
        Assert.Equal("Account not found", r.Error);
    }
}
