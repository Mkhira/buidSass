namespace BackendApi.Modules.Shared;

/// <summary>
/// Resolve a B2B company id from a customer id (FR-016a). Spec 021 implements;
/// spec 023 consumes for B2B-scoping of the support agent queue.
/// Returns null for B2C customers.
/// </summary>
public interface ICompanyAccountQuery
{
    Task<Guid?> ResolveCompanyIdAsync(Guid customerId, CancellationToken ct);
}
