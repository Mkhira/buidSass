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
///
/// <para><see cref="MarketCode"/> identifies the catalog partition that owns
/// the archived SKU (ADR-010: every tenant-owned entity is partitioned by
/// market). Subscribers MUST scope their updates to the originating market so
/// a same-SKU row in another market is not affected.</para>
/// </summary>
public sealed record ProductArchived(
    Guid ProductId,
    string Sku,
    string MarketCode,
    Guid ArchivedBy,
    DateTimeOffset OccurredAt);
