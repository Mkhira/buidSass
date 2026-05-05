namespace BackendApi.Modules.Shared;

/// <summary>
/// Subscriber contract for spec 005 (Catalog) product-lifecycle events. Spec 005
/// publishes; spec 021's <c>ProductArchivedHandler</c> subscribes (and future
/// specs may add their own subscribers).
///
/// Declared in <c>Modules/Shared/</c> per the project-memory rule (cross-module
/// hook contracts live here so consumers don't take a runtime dependency on the
/// catalog module).
///
/// All implementations MUST be idempotent — events may be re-delivered after
/// crash recovery or transient bus failures.
/// </summary>
public interface IProductLifecycleSubscriber
{
    Task OnProductArchivedAsync(ProductArchived evt, CancellationToken ct);
}

/// <summary>
/// A SKU was archived in the catalog. Downstream modules MUST treat this as a
/// soft-delete: existing references stay valid for audit, but the SKU MUST NOT
/// be selectable for new operations (cart, quote authoring, restock, etc.).
/// </summary>
public sealed record ProductArchived(
    Guid ProductId,
    string Sku,
    Guid ArchivedBy,
    DateTimeOffset OccurredAt);
