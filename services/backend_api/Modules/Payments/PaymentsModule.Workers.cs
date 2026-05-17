using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

public static partial class PaymentsModule
{
    static partial void AddWorkers(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<Workers.PaymentDispatchWorker>();
        services.AddHostedService<Workers.PendingExpirationReconciler>();
        services.AddHostedService<Workers.BankTransferTimeoutReconciler>();
        services.AddHostedService<Workers.DailyReconciliationJob>();
        services.AddHostedService<Workers.ProviderHealthMonitor>();
        services.AddHostedService<Workers.PciScopeMonitor>();
        services.AddHostedService<Workers.WebhookReplayWorker>();
        services.AddHostedService<Workers.IdempotencyKeySweeperWorker>();
    }
}
