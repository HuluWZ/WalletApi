using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Domain;

namespace WalletApi.Infrastructure.Persistence;

public class WalletDb(DbContextOptions<WalletDb> options) : DbContext(options), IWalletDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<ScheduledTransfer> ScheduledTransfers => Set<ScheduledTransfer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        if (Database.IsNpgsql()) // Postgres xmin system column as optimistic-concurrency token
            b.Entity<Account>().Property(a => a.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        else
            b.Entity<Account>().Ignore(a => a.Version);
        // unique key makes retried transfers safe at the DB level
        b.Entity<Transfer>().HasIndex(t => t.IdempotencyKey).IsUnique();
        b.Entity<ScheduledTransfer>().HasIndex(s => new { s.IsActive, s.NextRunAt });
    }
}
