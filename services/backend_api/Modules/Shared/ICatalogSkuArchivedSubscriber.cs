namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 007-b cross-module hook (data-model §7).
/// Catalog (spec 005) publishes <see cref="CatalogSkuArchivedEvent"/> when a SKU is archived.
/// Pricing subscribes via <see cref="ICatalogSkuArchivedSubscriber"/> to mark referencing
/// Coupons/Promotions/BusinessPricingRows with <c>applies_to_broken=true</c> (FR-034a).
/// </summary>
public interface ICatalogSkuArchivedSubscriber
{
    Task HandleAsync(CatalogSkuArchivedEvent e, CancellationToken ct);
}

public interface ICatalogSkuArchivedPublisher
{
    Task PublishAsync(CatalogSkuArchivedEvent e, CancellationToken ct);
}

public sealed record CatalogSkuArchivedEvent(
    Guid SkuId,
    string Sku,
    DateTimeOffset ArchivedAtUtc,
    Guid ActorId);
