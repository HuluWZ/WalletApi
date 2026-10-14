using Microsoft.Extensions.DependencyInjection;
using WalletApi.Application.Auth;
using WalletApi.Application.Scheduling;
using WalletApi.Application.Transfers;

namespace WalletApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s) => s
        .AddScoped<AuthService>()
        .AddScoped<TransferService>()
        .AddScoped<ScheduledTransferService>();
}
