using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WalletApi.Domain;
using WalletApi.Infrastructure.Persistence;

namespace WalletApi.Application.Tests;

/// <summary>In-memory SQLite database seeded with two accounts (A owned by <see cref="UserId"/> with 1000, B with 0).</summary>
public sealed class TestDb : IDisposable
{
    readonly SqliteConnection conn = new("DataSource=:memory:");
    public WalletDb Db { get; }
    public Guid UserId { get; } = Guid.NewGuid();
    public Account A { get; }
    public Account B { get; }

    public TestDb()
    {
        conn.Open();
        Db = new WalletDb(new DbContextOptionsBuilder<WalletDb>().UseSqlite(conn).Options);
        Db.Database.EnsureCreated();
        A = new Account { UserId = UserId, Balance = 1000 };
        B = new Account { UserId = Guid.NewGuid(), Balance = 0 };
        Db.Accounts.AddRange(A, B);
        Db.SaveChanges();
    }

    public void Dispose()
    {
        Db.Dispose();
        conn.Dispose();
    }
}
