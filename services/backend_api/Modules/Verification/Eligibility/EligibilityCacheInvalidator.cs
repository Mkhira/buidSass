using BackendApi.Modules.Verification.Persistence;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Rebuilds the <c>verification_eligibility_cache</c> row for a customer inside
/// the same Tx that performs a state transition (data-model §2.6 write path,
/// FR-024). No I/O outside the passed <see cref="VerificationDbContext"/>.
/// </summary>
/// <remarks>
/// Phase 2 ships the contract + a stub. Phase 5 (US3 — eligibility query) lands
/// the full read-and-UPSERT logic that joins the customer's active approval
/// against <c>IProductRestrictionPolicy</c>. The Phase 2 stub is intentionally
/// a no-op so the contract is callable from state-transition handlers in Phase 3
/// before the full eligibility surface is in place.
/// </remarks>
public sealed class EligibilityCacheInvalidator
{
    /// <summary>
    /// Rebuilds the eligibility row for a customer.
    /// </summary>
    /// <param name="customerId">Customer whose eligibility row should be recomputed.</param>
    /// <param name="db">DbContext participating in the caller's transaction.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task RebuildAsync(Guid customerId, VerificationDbContext db, CancellationToken ct)
    {
        // Phase 2 stub. Phase 5 (Eligibility/) replaces this with the
        // authoritative read-and-UPSERT per data-model §2.6.
        _ = customerId;
        _ = db;
        _ = ct;
        return Task.CompletedTask;
    }
}
