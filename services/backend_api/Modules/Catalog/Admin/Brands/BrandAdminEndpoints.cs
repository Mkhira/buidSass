using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.Brands;

public static class BrandAdminEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/brands", CreateAsync).RequireAuthorization(authorize).RequirePermission("catalog.brand.write");
        builder.MapPatch("/brands/{id:guid}", UpdateAsync).RequireAuthorization(authorize).RequirePermission("catalog.brand.write");
        return builder;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateBrandRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CreateBrandRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.brand.invalid_request",
                "Invalid brand request",
                validation.Errors.First().ErrorMessage);
        }

        var slug = AdminCatalogResponseFactory.NormalizeSlug(request.Slug);
        var duplicate = await dbContext.Brands.AnyAsync(b => b.Slug == slug, cancellationToken);
        if (duplicate)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.brand.slug_conflict",
                "Brand slug already exists",
                "A brand with that slug already exists.");
        }

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            IsActive = true,
        };
        dbContext.Brands.Add(brand);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.brand.created",
                EntityType: nameof(Brand),
                EntityId: brand.Id,
                BeforeState: null,
                AfterState: new { brand.Slug, brand.NameAr, brand.NameEn },
                Reason: "catalog.brand.create"),
            cancellationToken);

        return Results.Created($"/v1/admin/catalog/brands/{brand.Id:N}", new { brandId = brand.Id });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid id,
        UpdateBrandRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var brand = await dbContext.Brands.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.brand.not_found",
                "Brand not found",
                "The brand could not be found.");
        }

        var before = new { brand.NameAr, brand.NameEn, brand.IsActive };
        if (request.NameAr is not null) brand.NameAr = request.NameAr.Trim();
        if (request.NameEn is not null) brand.NameEn = request.NameEn.Trim();
        if (request.IsActive is bool active) brand.IsActive = active;
        brand.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.brand.updated",
                EntityType: nameof(Brand),
                EntityId: brand.Id,
                BeforeState: before,
                AfterState: new { brand.NameAr, brand.NameEn, brand.IsActive },
                Reason: "catalog.brand.update"),
            cancellationToken);

        return Results.NoContent();
    }
}

public sealed record CreateBrandRequest(string Slug, string NameAr, string NameEn);
public sealed record UpdateBrandRequest(string? NameAr, string? NameEn, bool? IsActive);

public sealed class CreateBrandRequestValidator : AbstractValidator<CreateBrandRequest>
{
    public CreateBrandRequestValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}
