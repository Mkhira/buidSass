namespace BackendApi.Modules.Shared;

/// <summary>
/// Read-side query that surfaces the two facts <see cref="Reviews.Primitives.QualifiedReporterPolicy"/>
/// needs to evaluate FR-023 — account age and "has at least one delivered, non-refunded
/// order anywhere" (NOT scoped to a particular product). Spec 004 supplies the
/// account-age input via the customer registration timestamp; spec 011 supplies the
/// has-delivered-order input via the orders read-side. The composed implementation lives
/// alongside <see cref="ICustomerVerificationEligibilityQuery"/>'s real binding.
/// </summary>
public interface IReviewReporterFactsQuery
{
    Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct);
}

/// <summary>
/// Snapshot of the FR-023 inputs for a single reporter at the moment of the report.
/// Stored verbatim on <see cref="Reviews.Entities.ReviewFlag.QualifyingEvaluationJson"/>
/// per data-model §2.4 / R5 so the threshold evaluation is reproducible during dispute audit.
/// </summary>
public sealed record ReviewReporterFacts(int AccountAgeDays, bool HasDeliveredOrder);
