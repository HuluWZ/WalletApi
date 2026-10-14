using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalletApi.Application.Abstractions;
using WalletApi.Infrastructure.Auth;
using WalletApi.Infrastructure.Background;
using WalletApi.Infrastructure.Persistence;

namespace WalletApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg) => s
        .AddDbContext<WalletDb>(o => o.UseNpgsql(cfg.GetConnectionString("Default")))
        .AddScoped<IWalletDb>(sp => sp.GetRequiredService<WalletDb>())
        .AddSingleton<ITokenService, TokenService>()
        .AddHostedService<ScheduledTransferWorker>();
}
