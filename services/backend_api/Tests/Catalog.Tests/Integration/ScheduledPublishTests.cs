using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Integration;

[Collection("catalog-fixture")]
public sealed class ScheduledPublishTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task ScheduledPublish_RowForDueProduct_LandsInTable()
    {
        await factory.ResetDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
        var productId = await CatalogTestSeedHelper.CreateProductAsync(scope.ServiceProvider, brandId, status: "scheduled");

        dbContext.ScheduledPublishes.Add(new ScheduledPublish
        {
            ProductId = productId,
            PublishAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ScheduledByAccountId = Guid.NewGuid(),
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await dbContext.SaveChangesAsync();

        var row = await dbContext.ScheduledPublishes.SingleAsync(s => s.ProductId == productId);
        row.WorkerClaimedAt.Should().BeNull();
        row.WorkerCompletedAt.Should().BeNull();
    }
}
