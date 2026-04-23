using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Admin.Products;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.BulkImportProducts;

public static class BulkImportEndpoint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/products/bulk-import", HandleAsync).RequireAuthorization(authorize).RequirePermission("catalog.bulk_import");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var contentType = context.Request.ContentType ?? string.Empty;
        if (!contentType.Contains("application/x-ndjson", StringComparison.OrdinalIgnoreCase))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "catalog.bulk.unsupported_media_type",
                "Unsupported media type",
                "Request Content-Type must be application/x-ndjson.");
        }

        context.Response.ContentType = "application/x-ndjson";
        await using var writer = new StreamWriter(context.Response.Body, Encoding.UTF8, leaveOpen: true);

        var idempotencyKey = context.Request.Headers["X-Idempotency-Key"].ToString();
        var hasIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey);

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var validator = new CreateProductRequestValidator();
        var rowIndex = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rowIndex++;
            CreateProductRequest? payload;
            try
            {
                payload = JsonSerializer.Deserialize<CreateProductRequest>(line, SerializerOptions);
            }
            catch (JsonException ex)
            {
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, ex.Message));
                continue;
            }

            if (payload is null)
            {
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, "Empty row."));
                continue;
            }

            // Per-row idempotency: if the same (idempotencyKey, rowIndex) was processed before,
            // short-circuit with the original verdict. Keyed hash so we don't store raw headers.
            byte[]? rowHash = null;
            if (hasIdempotencyKey)
            {
                rowHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{idempotencyKey}::{rowIndex}"));
                var existing = await dbContext.BulkImportIdempotency.SingleOrDefaultAsync(x => x.RowHash == rowHash, cancellationToken);
                if (existing is not null)
                {
                    await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, existing.Status, existing.ProductId, existing.Status == "error" ? "catalog.bulk.row_idempotent_duplicate" : null));
                    continue;
                }
            }

            var validation = await validator.ValidateAsync(payload, cancellationToken);
            if (!validation.IsValid)
            {
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, validation.Errors.First().ErrorMessage));
                continue;
            }

            if (await dbContext.Products.AnyAsync(p => p.Sku == payload.Sku.Trim(), cancellationToken))
            {
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, "catalog.product.sku_conflict"));
                continue;
            }

            if (!await dbContext.Brands.AnyAsync(b => b.Id == payload.BrandId, cancellationToken))
            {
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, "catalog.brand.unknown"));
                continue;
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = payload.Sku.Trim(),
                Barcode = payload.Barcode,
                BrandId = payload.BrandId,
                ManufacturerId = payload.ManufacturerId,
                SlugAr = AdminCatalogResponseFactory.NormalizeSlug(payload.SlugAr),
                SlugEn = AdminCatalogResponseFactory.NormalizeSlug(payload.SlugEn),
                NameAr = payload.NameAr.Trim(),
                NameEn = payload.NameEn.Trim(),
                ShortDescriptionAr = payload.ShortDescriptionAr,
                ShortDescriptionEn = payload.ShortDescriptionEn,
                DescriptionAr = payload.DescriptionAr,
                DescriptionEn = payload.DescriptionEn,
                AttributesJson = payload.AttributesJson ?? "{}",
                MarketCodes = (payload.MarketCodes ?? Array.Empty<string>()).Select(m => m.Trim().ToLowerInvariant()).ToArray(),
                Status = "draft",
                Restricted = payload.Restricted ?? false,
                RestrictionReasonCode = payload.RestrictionReasonCode,
                RestrictionMarkets = (payload.RestrictionMarkets ?? Array.Empty<string>()).Select(m => m.Trim().ToLowerInvariant()).ToArray(),
                PriceHintMinorUnits = payload.PriceHintMinorUnits,
                CreatedByAccountId = AdminCatalogResponseFactory.ResolveActorAccountId(context),
            };
            dbContext.Products.Add(product);

            try
            {
                if (rowHash is not null)
                {
                    dbContext.BulkImportIdempotency.Add(new BulkImportIdempotencyRecord
                    {
                        RowHash = rowHash,
                        ProductId = product.Id,
                        Status = "ok",
                    });
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "ok", product.Id, null));
            }
            catch (DbUpdateException ex)
            {
                dbContext.Entry(product).State = EntityState.Detached;
                await WriteJsonLineAsync(writer, new BulkImportRowResult(rowIndex, "error", null, ex.InnerException?.Message ?? ex.Message));
            }
        }

        await writer.FlushAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.products.bulk_import",
                EntityType: "bulk_import",
                EntityId: Guid.NewGuid(),
                BeforeState: null,
                AfterState: new { ProcessedRows = rowIndex },
                Reason: "catalog.bulk_import"),
            cancellationToken);

        return Results.Empty;
    }

    private static async Task WriteJsonLineAsync(StreamWriter writer, BulkImportRowResult row)
    {
        var line = JsonSerializer.Serialize(row, SerializerOptions);
        await writer.WriteLineAsync(line);
        await writer.FlushAsync();
    }
}

public sealed record BulkImportRowResult(int RowIndex, string Status, Guid? ProductId, string? Error);
