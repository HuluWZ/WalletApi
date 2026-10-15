using Microsoft.Extensions.DependencyInjection;
using WalletApi.Application.Auth;
using WalletApi.Application.Compliance;
using WalletApi.Application.Credit;
using WalletApi.Application.Lending;
using WalletApi.Application.Scheduling;
using WalletApi.Application.Transfers;

namespace WalletApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s) => s
        .AddScoped<AuthService>()
        .AddScoped<TransferService>()
        .AddScoped<ComplianceService>()
        .AddScoped<CreditScoreService>()
        .AddScoped<LoanService>()
        .AddScoped<ScheduledTransferService>();
}
