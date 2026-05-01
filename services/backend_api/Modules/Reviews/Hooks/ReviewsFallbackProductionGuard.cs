using BackendApi.Modules.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Startup-time fail-fast guard — refuses to start the host in Production if
/// either cross-module fallback (NullOrderLineDeliveryEligibilityQuery /
/// NullReviewReporterFactsQuery) is still bound. Without this guard the docs
/// alone cannot prevent a silent prod misconfig where review submission is
/// disabled or community-report escalation never fires.
/// </summary>
public sealed class ReviewsFallbackProductionGuard : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _env;

    public ReviewsFallbackProductionGuard(IServiceProvider services, IHostEnvironment env)
    {
        _services = services;
        _env = env;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_env.IsProduction()) return Task.CompletedTask;

        using var scope = _services.CreateScope();
        var elig = scope.ServiceProvider.GetRequiredService<IOrderLineDeliveryEligibilityQuery>();
        if (elig is NullOrderLineDeliveryEligibilityQuery)
        {
            throw new InvalidOperationException(
                "ReviewsFallbackProductionGuard: NullOrderLineDeliveryEligibilityQuery is bound in Production. " +
                "Spec 011 must register its real IOrderLineDeliveryEligibilityQuery before AddReviewsModule, " +
                "otherwise review submission is silently disabled.");
        }

        var facts = scope.ServiceProvider.GetRequiredService<IReviewReporterFactsQuery>();
        if (facts is NullReviewReporterFactsQuery)
        {
            throw new InvalidOperationException(
                "ReviewsFallbackProductionGuard: NullReviewReporterFactsQuery is bound in Production. " +
                "Specs 004+011 must register the real composed IReviewReporterFactsQuery, " +
                "otherwise community-report threshold escalation never fires.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
