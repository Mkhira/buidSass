using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Integration;

[Collection("catalog-fixture")]
public sealed class LocaleFallbackTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task LocaleFallback_MissingEn_FallsBackToArWithHeader()
    {
        await factory.ResetDatabaseAsync();
        string slug;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = "locale-fb-1",
                BrandId = brandId,
                SlugAr = "منتج-عربي",
                SlugEn = "slug-en",
                NameAr = "اسم عربي",
                NameEn = string.Empty,
                ShortDescriptionAr = "وصف مختصر",
                MarketCodes = new[] { "ksa" },
                Status = "published",
                PublishedAt = DateTimeOffset.UtcNow,
                CreatedByAccountId = Guid.NewGuid(),
            };
            dbContext.Products.Add(product);
            dbContext.ProductMedia.Add(new ProductMedia
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                StorageKey = "k",
                ContentSha256 = new byte[32],
                MimeType = "image/jpeg",
                IsPrimary = true,
                VariantStatus = "ready",
            });
            await dbContext.SaveChangesAsync();
            slug = product.SlugEn;
        }

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/customer/catalog/products/{slug}?market=ksa");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("x-locale-fallback", out var fallback).Should().BeTrue();
        string.Join(",", fallback!).Should().Contain("name:ar");
    }
}
