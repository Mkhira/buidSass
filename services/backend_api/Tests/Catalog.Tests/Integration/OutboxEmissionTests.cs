using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives.Outbox;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Integration;

/// <summary>
/// Exercises the outbox seam directly (no HTTP): every `catalog.product.*` state change should
/// leave a row in `catalog.catalog_outbox` that the dispatcher worker will pick up. Downstream
/// spec 006 (search) consumes the same row shape.
/// </summary>
[Collection("catalog-fixture")]
public sealed class OutboxEmissionTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task Publish_EmitsOutboxRowForSearch()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();

        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var productId = await CatalogTestSeedHelper.CreateProductAsync(
            scope.ServiceProvider,
            brandId,
            sku: "publish-outbox-1",
            status: "in_review",
            hasPrimaryMedia: true);

        outboxWriter.Enqueue("catalog.product.published", productId, new { productId, sku = "publish-outbox-1" });
        await dbContext.SaveChangesAsync();

        var rows = await dbContext.CatalogOutbox
            .Where(o => o.AggregateId == productId && o.EventType == "catalog.product.published")
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].DispatchedAt.Should().BeNull();
        rows[0].PayloadJson.Should().Contain("publish-outbox-1");
    }

    [Fact]
    public async Task RestrictionChange_EmitsOutboxRow()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<CatalogOutboxWriter>();

        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var productId = await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId);

        outboxWriter.Enqueue("catalog.product.restriction_changed", productId, new { productId, restricted = true });
        await dbContext.SaveChangesAsync();

        var row = await dbContext.CatalogOutbox.SingleAsync(o => o.AggregateId == productId && o.EventType == "catalog.product.restriction_changed");
        row.DispatchedAt.Should().BeNull();
    }
}
