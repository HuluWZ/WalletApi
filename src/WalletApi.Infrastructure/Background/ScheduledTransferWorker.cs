using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WalletApi.Application.Lending;
using WalletApi.Application.Scheduling;

namespace WalletApi.Infrastructure.Background;

public class ScheduledTransferWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<ScheduledTransferWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.GetValue("Scheduler:TickSeconds", 30)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var now = DateTime.UtcNow;
                var ran = await scope.ServiceProvider.GetRequiredService<ScheduledTransferService>().RunDueAsync(now);
                if (ran > 0) log.LogInformation("Processed {Count} scheduled transfers", ran);
                var repaid = await scope.ServiceProvider.GetRequiredService<LoanService>().RunDueAsync(now);
                if (repaid > 0) log.LogInformation("Collected {Count} loan installments", repaid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Scheduled transfer run failed"); // next tick retries
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
