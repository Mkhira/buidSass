using BackendApi.Modules.Reviews.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial for the daily reconciliation + integrity-scan workers
/// (Phase L). Both workers are advisory-locked via <see cref="ReviewsAdvisoryLock"/>
/// so multi-replica deployments don't double-execute.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddWorkers(IServiceCollection services)
    {
        services.AddOptions<ReviewsWorkerOptions>()
            .BindConfiguration(ReviewsWorkerOptions.SectionName)
            .Validate(o =>
            {
                // Fail fast on misconfigured schedules instead of letting
                // Period<=0 spin a hot loop or InitialDelay<0 throw at runtime.
                o.RatingAggregateRebuild.Validate($"{ReviewsWorkerOptions.SectionName}:RatingAggregateRebuild");
                o.ReviewIntegrityScan.Validate($"{ReviewsWorkerOptions.SectionName}:ReviewIntegrityScan");
                return true;
            }, "Reviews worker schedules must have Period > 0 and InitialDelay >= 0.")
            .ValidateOnStart();

        services.TryAddScoped<IRefundedOrderLineLookup, NullRefundedOrderLineLookup>();

        // Singleton + hosted-service registration: the singleton is what tests
        // resolve to call ExecuteOnceAsync; the hosted-service registration
        // ensures the platform host runs ExecuteAsync on startup.
        services.AddSingleton<RatingAggregateRebuildWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<RatingAggregateRebuildWorker>());

        services.AddSingleton<ReviewIntegrityScanWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ReviewIntegrityScanWorker>());
    }
}
