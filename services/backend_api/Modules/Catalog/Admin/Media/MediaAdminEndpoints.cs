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
using SixLabors.ImageSharp;

namespace BackendApi.Modules.Catalog.Admin.Media;

public static class MediaAdminEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/products/{id:guid}/media", UploadAsync).RequireAuthorization(authorize).RequirePermission("catalog.media.write").DisableAntiforgery();
        builder.MapPatch("/products/{id:guid}/media/{mediaId:guid}", UpdateAsync).RequireAuthorization(authorize).RequirePermission("catalog.media.write");
        builder.MapDelete("/products/{id:guid}/media/{mediaId:guid}", DeleteAsync).RequireAuthorization(authorize).RequirePermission("catalog.media.write");
        return builder;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        Guid id,
        IFormFile file,
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
                "catalog.media.empty_upload",
                "Empty upload",
                "An image file is required.");
        }

        var product = await dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.product.not_found",
                "Product not found",
                "The product could not be found.");
        }

        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var sha = paths.ComputeSha256(bytes);

        int widthPx;
        int heightPx;
        try
        {
            using var image = Image.Load(bytes);
            widthPx = image.Width;
            heightPx = image.Height;
        }
        catch (Exception)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.media.invalid_image",
                "Invalid image",
                "The uploaded file is not a readable image.");
        }

        var ext = Path.GetExtension(file.FileName).TrimStart('.');
        var storageKey = paths.OriginalKey(id, sha, string.IsNullOrWhiteSpace(ext) ? "bin" : ext);

        var media = new ProductMedia
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            StorageKey = storageKey,
            ContentSha256 = sha,
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Bytes = file.Length,
            WidthPx = widthPx,
            HeightPx = heightPx,
            DisplayOrder = 0,
            IsPrimary = false,
            VariantStatus = "pending",
        };
        dbContext.ProductMedia.Add(media);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.media.uploaded",
                EntityType: nameof(ProductMedia),
                EntityId: media.Id,
                BeforeState: null,
                AfterState: new { media.ProductId, media.StorageKey, media.Bytes, media.WidthPx, media.HeightPx },
                Reason: "catalog.media.upload"),
            cancellationToken);

        return Results.Accepted(value: new { mediaId = media.Id, variantStatus = media.VariantStatus });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid id,
        Guid mediaId,
        UpdateMediaRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var media = await dbContext.ProductMedia.SingleOrDefaultAsync(m => m.Id == mediaId && m.ProductId == id, cancellationToken);
        if (media is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.media.not_found",
                "Media not found",
                "The media row could not be found.");
        }

        if (request.AltAr is not null) media.AltAr = request.AltAr;
        if (request.AltEn is not null) media.AltEn = request.AltEn;
        if (request.DisplayOrder is int order) media.DisplayOrder = order;
        if (request.IsPrimary is bool primary)
        {
            if (primary)
            {
                var others = await dbContext.ProductMedia.Where(m => m.ProductId == id && m.IsPrimary && m.Id != mediaId).ToListAsync(cancellationToken);
                foreach (var o in others) o.IsPrimary = false;
            }
            media.IsPrimary = primary;
        }

        media.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.media.updated",
                EntityType: nameof(ProductMedia),
                EntityId: media.Id,
                BeforeState: null,
                AfterState: new { media.AltAr, media.AltEn, media.IsPrimary, media.DisplayOrder },
                Reason: "catalog.media.update"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context,
        Guid id,
        Guid mediaId,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var media = await dbContext.ProductMedia.SingleOrDefaultAsync(m => m.Id == mediaId && m.ProductId == id, cancellationToken);
        if (media is null)
        {
            return Results.NoContent();
        }

        dbContext.ProductMedia.Remove(media);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.media.deleted",
                EntityType: nameof(ProductMedia),
                EntityId: media.Id,
                BeforeState: new { media.ProductId, media.StorageKey },
                AfterState: null,
                Reason: "catalog.media.delete"),
            cancellationToken);

        return Results.NoContent();
    }
}

public sealed record UpdateMediaRequest(string? AltAr, string? AltEn, int? DisplayOrder, bool? IsPrimary);
