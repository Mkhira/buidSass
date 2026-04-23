using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.Manufacturers;

public static class ManufacturerAdminEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/manufacturers", CreateAsync).RequireAuthorization(authorize).RequirePermission("catalog.manufacturer.write");
        builder.MapPatch("/manufacturers/{id:guid}", UpdateAsync).RequireAuthorization(authorize).RequirePermission("catalog.manufacturer.write");
        return builder;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateManufacturerRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.manufacturer.invalid_request",
                "Invalid manufacturer request",
                "slug, nameAr, and nameEn are required.");
        }

        var slug = AdminCatalogResponseFactory.NormalizeSlug(request.Slug);
        if (await dbContext.Manufacturers.AnyAsync(m => m.Slug == slug, cancellationToken))
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.manufacturer.slug_conflict",
                "Manufacturer slug already exists",
                "A manufacturer with that slug already exists.");
        }

        var manufacturer = new Manufacturer
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            IsActive = true,
        };
        dbContext.Manufacturers.Add(manufacturer);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.manufacturer.created",
                EntityType: nameof(Manufacturer),
                EntityId: manufacturer.Id,
                BeforeState: null,
                AfterState: new { manufacturer.Slug },
                Reason: "catalog.manufacturer.create"),
            cancellationToken);

        return Results.Created($"/v1/admin/catalog/manufacturers/{manufacturer.Id:N}", new { manufacturerId = manufacturer.Id });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid id,
        UpdateManufacturerRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var manufacturer = await dbContext.Manufacturers.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (manufacturer is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.manufacturer.not_found",
                "Manufacturer not found",
                "The manufacturer could not be found.");
        }

        if (request.NameAr is not null) manufacturer.NameAr = request.NameAr.Trim();
        if (request.NameEn is not null) manufacturer.NameEn = request.NameEn.Trim();
        if (request.IsActive is bool active) manufacturer.IsActive = active;
        manufacturer.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.manufacturer.updated",
                EntityType: nameof(Manufacturer),
                EntityId: manufacturer.Id,
                BeforeState: null,
                AfterState: new { manufacturer.NameAr, manufacturer.NameEn, manufacturer.IsActive },
                Reason: "catalog.manufacturer.update"),
            cancellationToken);

        return Results.NoContent();
    }
}

public sealed record CreateManufacturerRequest(string Slug, string NameAr, string NameEn);
public sealed record UpdateManufacturerRequest(string? NameAr, string? NameEn, bool? IsActive);
