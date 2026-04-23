using BackendApi.Modules.Catalog.Primitives;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Unit;

/// <summary>
/// Property tests on the closure-table maintained by <see cref="CategoryTreeService"/>. Uses the
/// real Postgres fixture rather than in-memory because the service's queries depend on Postgres
/// semantics (`citext` uniqueness, jsonb not relevant here but consistency matters).
/// </summary>
[Collection("catalog-fixture")]
public sealed class CategoryTreeInvariantsTests(CatalogTestFactory factory)
{
    [Property(Arbitrary = new[] { typeof(SmallPositiveIntGen) }, MaxTest = 10)]
    public void Insert_ChainOfN_ClosureRowCountIsTriangular(int depth)
    {
        if (depth < 1) return;

        factory.ResetDatabaseAsync().GetAwaiter().GetResult();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Persistence.CatalogDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CategoryTreeService>();

        Guid? parentId = null;
        for (var i = 0; i < depth; i++)
        {
            var id = Guid.NewGuid();
            dbContext.Categories.Add(new BackendApi.Modules.Catalog.Entities.Category
            {
                Id = id,
                Slug = $"chain-{depth}-{i}-{Guid.NewGuid():N}",
                ParentId = parentId,
                NameAr = $"c{i}",
                NameEn = $"c{i}",
            });
            dbContext.SaveChanges();
            svc.InsertAsync(dbContext, id, parentId, CancellationToken.None).GetAwaiter().GetResult();
            dbContext.SaveChanges();
            parentId = id;
        }

        dbContext.CategoryClosures.Count().Should().Be(depth * (depth + 1) / 2);
    }

    [Fact]
    public async Task Reparent_AttachingToOwnDescendant_IsRejectedAsCycle()
    {
        await factory.ResetDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Catalog.Persistence.CatalogDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<CategoryTreeService>();

        var rootId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "root");
        var childId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "child", parentId: rootId);
        var grandchildId = await CatalogTestSeedHelper.CreateCategoryAsync(scope.ServiceProvider, "grandchild", parentId: childId);

        var result = await svc.ReparentAsync(dbContext, rootId, grandchildId, CancellationToken.None);

        result.Should().Be(ReparentResult.Cycle);
    }

    public static class SmallPositiveIntGen
    {
        public static Arbitrary<int> Int() => Gen.Choose(1, 8).ToArbitrary();
    }
}
