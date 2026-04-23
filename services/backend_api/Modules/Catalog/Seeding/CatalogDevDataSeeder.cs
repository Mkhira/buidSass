using BackendApi.Features.Seeding;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Catalog.Seeding;

public sealed class CatalogDevDataSeeder : ISeeder
{
    public string Name => "catalog.dev-data-v1";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => new[] { "catalog.category-attribute-schemas-v1" };

    public async Task ApplyAsync(SeedContext ctx, CancellationToken cancellationToken)
    {
        if (!ctx.Env.IsDevelopment())
        {
            ctx.Logger.LogInformation("catalog.dev-data skipped environment={Env}", ctx.Env.EnvironmentName);
            return;
        }

        var catalogDb = ctx.Services.GetRequiredService<CatalogDbContext>();
        var categoryTree = ctx.Services.GetRequiredService<CategoryTreeService>();

        var generalCategory = await catalogDb.Categories
            .SingleOrDefaultAsync(c => c.Slug == "general", cancellationToken);

        if (generalCategory is null)
        {
            generalCategory = new Category
            {
                Id = Guid.NewGuid(),
                Slug = "general",
                ParentId = null,
                NameAr = "عام",
                NameEn = "General",
                DisplayOrder = 0,
                IsActive = true,
            };
            catalogDb.Categories.Add(generalCategory);
            await catalogDb.SaveChangesAsync(cancellationToken);
            await categoryTree.InsertAsync(catalogDb, generalCategory.Id, null, cancellationToken);
            await catalogDb.SaveChangesAsync(cancellationToken);
        }

        if (!await catalogDb.Brands.AnyAsync(cancellationToken))
        {
            var brands = new[]
            {
                new Brand { Id = Guid.NewGuid(), Slug = "acme-medical", NameAr = "أكمي الطبية", NameEn = "Acme Medical" },
                new Brand { Id = Guid.NewGuid(), Slug = "dentkit", NameAr = "دنت كيت", NameEn = "DentKit" },
                new Brand { Id = Guid.NewGuid(), Slug = "lab-supply-co", NameAr = "لاب سبلاي", NameEn = "Lab Supply Co." },
            };
            catalogDb.Brands.AddRange(brands);
            await catalogDb.SaveChangesAsync(cancellationToken);
        }

        if (!await catalogDb.Manufacturers.AnyAsync(cancellationToken))
        {
            var manufacturers = new[]
            {
                new Manufacturer { Id = Guid.NewGuid(), Slug = "acme-labs", NameAr = "مختبرات أكمي", NameEn = "Acme Labs" },
                new Manufacturer { Id = Guid.NewGuid(), Slug = "dent-industries", NameAr = "دنت إندستريز", NameEn = "Dent Industries" },
            };
            catalogDb.Manufacturers.AddRange(manufacturers);
            await catalogDb.SaveChangesAsync(cancellationToken);
        }

        ctx.Logger.LogInformation("catalog.dev-data applied categories={Categories} brands={Brands}",
            await catalogDb.Categories.CountAsync(cancellationToken),
            await catalogDb.Brands.CountAsync(cancellationToken));
    }
}
