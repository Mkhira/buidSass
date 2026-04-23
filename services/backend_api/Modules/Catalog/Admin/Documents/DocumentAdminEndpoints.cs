using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.Documents;

public static class DocumentAdminEndpoints
{
    private static readonly HashSet<string> AllowedDocTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msds",
        "datasheet",
        "regulatory_cert",
        "ifu",
        "brochure",
    };

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/products/{id:guid}/documents", UploadAsync).RequireAuthorization(authorize).RequirePermission("catalog.document.write").DisableAntiforgery();
        return builder;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        Guid id,
        IFormFile file,
        string docType,
        string locale,
        string? titleAr,
        string? titleEn,
        CatalogDbContext dbContext,
        ContentAddressedPaths paths,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.document.empty_upload",
                "Empty upload",
                "A document file is required.");
        }

        if (string.IsNullOrWhiteSpace(docType) || !AllowedDocTypes.Contains(docType))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.document.invalid_type",
                "Invalid document type",
                "docType must be one of msds|datasheet|regulatory_cert|ifu|brochure.");
        }

        var normalizedLocale = locale?.Trim().ToLowerInvariant();
        if (normalizedLocale is not ("ar" or "en"))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.document.invalid_locale",
                "Invalid locale",
                "locale must be ar or en.");
        }

        await using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
        }
        var bytes = buffer.ToArray();
        var sha = paths.ComputeSha256(bytes);
        var ext = Path.GetExtension(file.FileName).TrimStart('.');
        var storageKey = paths.OriginalKey(id, sha, string.IsNullOrWhiteSpace(ext) ? "bin" : ext);

        var existing = await dbContext.ProductDocuments.SingleOrDefaultAsync(
            d => d.ProductId == id && d.DocType == docType && d.Locale == normalizedLocale,
            cancellationToken);
        if (existing is not null)
        {
            existing.StorageKey = storageKey;
            existing.ContentSha256 = sha;
            existing.TitleAr = titleAr ?? existing.TitleAr;
            existing.TitleEn = titleEn ?? existing.TitleEn;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEventPublisher.PublishAsync(new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.document.updated",
                EntityType: nameof(ProductDocument),
                EntityId: existing.Id,
                BeforeState: null,
                AfterState: new { existing.ProductId, existing.DocType, existing.Locale, existing.StorageKey },
                Reason: "catalog.document.update"), cancellationToken);
            return Results.Ok(new { documentId = existing.Id });
        }

        var document = new ProductDocument
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            DocType = docType.Trim().ToLowerInvariant(),
            Locale = normalizedLocale,
            StorageKey = storageKey,
            ContentSha256 = sha,
            TitleAr = titleAr,
            TitleEn = titleEn,
        };
        dbContext.ProductDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(new AuditEvent(
            ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
            ActorRole: "admin",
            Action: "catalog.document.uploaded",
            EntityType: nameof(ProductDocument),
            EntityId: document.Id,
            BeforeState: null,
            AfterState: new { document.ProductId, document.DocType, document.Locale, document.StorageKey },
            Reason: "catalog.document.upload"), cancellationToken);

        return Results.Created($"/v1/admin/catalog/products/{id:N}/documents/{document.Id:N}", new { documentId = document.Id });
    }
}
