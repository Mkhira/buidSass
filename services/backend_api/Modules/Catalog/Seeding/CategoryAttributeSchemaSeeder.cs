using System.Text.Json;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BackendApi.Modules.Catalog.Seeding;

public sealed class CategoryAttributeSchemaSeeder : ISeeder
{
    public string Name => "catalog.category-attribute-schemas-v1";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public async Task ApplyAsync(SeedContext ctx, CancellationToken cancellationToken)
    {
        var catalogDb = ctx.Services.GetRequiredService<CatalogDbContext>();
        var logger = ctx.Logger;

        var schemasDir = Path.Combine(AppContext.BaseDirectory, "Modules", "Catalog", "AttributeSchemas");
        if (!Directory.Exists(schemasDir))
        {
            schemasDir = Path.Combine(Directory.GetCurrentDirectory(), "Modules", "Catalog", "AttributeSchemas");
        }

        if (!Directory.Exists(schemasDir))
        {
            logger.LogWarning("catalog.seeding.schemas-dir-missing path={Path}", schemasDir);
            return;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var file in Directory.EnumerateFiles(schemasDir, "*.yaml"))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var payload = deserializer.Deserialize<AttributeSchemaFile>(text);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                continue;
            }

            var categorySlug = payload.Name.Trim().ToLowerInvariant();
            var category = await catalogDb.Categories
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.Slug == categorySlug, cancellationToken);

            if (category is null)
            {
                continue;
            }

            var schemaJson = JsonSerializer.Serialize(payload.Schema ?? new Dictionary<string, object?>());
            var existing = await catalogDb.CategoryAttributeSchemas
                .SingleOrDefaultAsync(s => s.CategoryId == category.Id, cancellationToken);

            if (existing is null)
            {
                catalogDb.CategoryAttributeSchemas.Add(new CategoryAttributeSchema
                {
                    CategoryId = category.Id,
                    SchemaJson = schemaJson,
                    Version = 1,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else if (!string.Equals(existing.SchemaJson, schemaJson, StringComparison.Ordinal))
            {
                existing.SchemaJson = schemaJson;
                existing.Version += 1;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await catalogDb.SaveChangesAsync(cancellationToken);
    }

    private sealed record AttributeSchemaFile
    {
        public string? Name { get; init; }
        public string? Title { get; init; }
        public Dictionary<string, object?>? Schema { get; init; }
    }
}
