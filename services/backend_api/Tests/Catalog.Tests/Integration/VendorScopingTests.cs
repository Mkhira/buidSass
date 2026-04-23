using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Integration;

[Collection("catalog-fixture")]
public sealed class VendorScopingTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task AdminList_DefaultsToVendorNullByQueryShape()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var platformProductId = await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, sku: "platform-1");

        // Simulate a future vendor-owned product — vendor_id is reserved but never set at launch.
        var vendorId = Guid.NewGuid();
        dbContext.Products.Add(new Product
        {
            Id = Guid.NewGuid(),
            Sku = "vendor-1",
            BrandId = brandId,
            SlugAr = "vendor-1-ar",
            SlugEn = "vendor-1-en",
            NameAr = "منتج",
            NameEn = "Product",
            Status = "published",
            MarketCodes = new[] { "ksa" },
            VendorId = vendorId,
            CreatedByAccountId = Guid.NewGuid(),
            PublishedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var platformOnly = await dbContext.Products.Where(p => p.VendorId == null).ToListAsync();
        platformOnly.Should().ContainSingle(p => p.Id == platformProductId);
        platformOnly.Should().NotContain(p => p.Sku == "vendor-1");
    }
}
