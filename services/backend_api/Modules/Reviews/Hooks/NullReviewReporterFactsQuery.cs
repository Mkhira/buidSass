using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Fallback for <see cref="IReviewReporterFactsQuery"/> shipped while specs 004 + 011
/// integrate. Returns the conservative "brand-new, never delivered" facts so reports
/// default to <c>is_qualified=false</c> and never trip the threshold escalation.
///
/// Spec 004 / 011 supplies the production binding via <c>TryAddScoped</c>; runtime swap
/// is automatic once those PRs land.
/// </summary>
public sealed class NullReviewReporterFactsQuery : IReviewReporterFactsQuery
{
    public Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct) =>
        Task.FromResult(new ReviewReporterFacts(AccountAgeDays: 0, HasDeliveredOrder: false));
}
