using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Fail-closed fallback for <see cref="IReviewReporterFactsQuery"/> shipped
/// while specs 004 + 011 integrate. Returns the conservative
/// "brand-new, never-delivered" facts so reports default to
/// <c>is_qualified = false</c> — meaning the threshold escalation
/// (<c>visible → flagged</c>) WILL NEVER FIRE while this fallback is bound.
///
/// <para><b>Production-deploy gate</b>: shipping this binding to production
/// silently disables community-report-based moderation escalation. Each
/// call emits an <see cref="LogLevel.Warning"/> log so monitoring catches
/// the misconfiguration. Operators MUST replace this binding with the
/// composed spec 004 + 011 implementation before launching the review
/// surface to customers.</para>
///
/// <para>Replacement is automatic on DI resolution: the production binding
/// registers via <c>TryAddScoped</c> before this fallback's <c>TryAdd</c>
/// short-circuits.</para>
/// </summary>
public sealed class NullReviewReporterFactsQuery : IReviewReporterFactsQuery
{
    private readonly ILogger<NullReviewReporterFactsQuery>? _logger;

    public NullReviewReporterFactsQuery(ILogger<NullReviewReporterFactsQuery>? logger = null)
    {
        _logger = logger;
    }

    public Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct)
    {
        _logger?.LogWarning(
            "NullReviewReporterFactsQuery hit for customer {CustomerId} — community-report threshold escalation disabled until specs 004 + 011 ship their composed reporter-facts binding.",
            customerId);
        return Task.FromResult(new ReviewReporterFacts(AccountAgeDays: 0, HasDeliveredOrder: false));
    }
}
