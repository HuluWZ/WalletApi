using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WalletApi.Domain;

namespace WalletApi.Application.Abstractions;

public interface IWalletDb
{
    DbSet<User> Users { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Transfer> Transfers { get; }
    DbSet<ScheduledTransfer> ScheduledTransfers { get; }
    DbSet<ComplianceFlag> ComplianceFlags { get; }
    DbSet<Loan> Loans { get; }
    DbSet<LoanInstallment> LoanInstallments { get; }
    DbSet<CreditScoreSnapshot> CreditScoreSnapshots { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ITokenService
{
    string Create(Guid userId, string email, Role role);
}
