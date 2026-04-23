using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Catalog.Admin.Common;
using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Catalog.Primitives;
using BackendApi.Modules.Identity.Authorization.Filters;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Admin.Categories;

public static class CategoryAdminEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder builder)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        builder.MapPost("/categories", CreateAsync).RequireAuthorization(authorize).RequirePermission("catalog.category.write");
        builder.MapPatch("/categories/{id:guid}", UpdateAsync).RequireAuthorization(authorize).RequirePermission("catalog.category.write");
        builder.MapPost("/categories/{id:guid}/reparent", ReparentAsync).RequireAuthorization(authorize).RequirePermission("catalog.category.write");
        builder.MapDelete("/categories/{id:guid}", DeleteAsync).RequireAuthorization(authorize).RequirePermission("catalog.category.write");

        return builder;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateCategoryRequest request,
        CatalogDbContext dbContext,
        CategoryTreeService categoryTree,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new CreateCategoryRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.category.invalid_request",
                "Invalid category request",
                validation.Errors.First().ErrorMessage);
        }

        var slug = AdminCatalogResponseFactory.NormalizeSlug(request.Slug);
        var duplicate = await dbContext.Categories
            .AnyAsync(c => c.ParentId == request.ParentId && c.Slug == slug, cancellationToken);
        if (duplicate)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.category.slug_conflict",
                "Category slug already exists",
                "A category with that slug already exists under the same parent.");
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            ParentId = request.ParentId,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DisplayOrder = request.DisplayOrder ?? 0,
            IsActive = true,
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        await categoryTree.InsertAsync(dbContext, category.Id, category.ParentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.category.created",
                EntityType: nameof(Category),
                EntityId: category.Id,
                BeforeState: null,
                AfterState: new { category.Slug, category.ParentId, category.NameAr, category.NameEn },
                Reason: "catalog.category.create"),
            cancellationToken);

        return Results.Created($"/v1/admin/catalog/categories/{category.Id:N}", new { categoryId = category.Id });
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid id,
        UpdateCategoryRequest request,
        CatalogDbContext dbContext,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var validator = new UpdateCategoryRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status400BadRequest,
                "catalog.category.invalid_request",
                "Invalid category request",
                validation.Errors.First().ErrorMessage);
        }

        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.category.not_found",
                "Category not found",
                "The category could not be found.");
        }

        var before = new { category.NameAr, category.NameEn, category.DisplayOrder, category.IsActive };
        if (request.NameAr is not null) category.NameAr = request.NameAr.Trim();
        if (request.NameEn is not null) category.NameEn = request.NameEn.Trim();
        if (request.DisplayOrder is int order) category.DisplayOrder = order;
        if (request.IsActive is bool active) category.IsActive = active;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.category.updated",
                EntityType: nameof(Category),
                EntityId: category.Id,
                BeforeState: before,
                AfterState: new { category.NameAr, category.NameEn, category.DisplayOrder, category.IsActive },
                Reason: "catalog.category.update"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ReparentAsync(
        HttpContext context,
        Guid id,
        ReparentCategoryRequest request,
        CatalogDbContext dbContext,
        CategoryTreeService categoryTree,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status404NotFound,
                "catalog.category.not_found",
                "Category not found",
                "The category could not be found.");
        }

        var previousParent = category.ParentId;
        var result = await categoryTree.ReparentAsync(dbContext, id, request.NewParentId, cancellationToken);
        if (result == ReparentResult.Cycle)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.category.cycle_detected",
                "Reparent would create a cycle",
                "The requested reparent would introduce a cycle in the category tree.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.category.reparented",
                EntityType: nameof(Category),
                EntityId: id,
                BeforeState: new { PreviousParentId = previousParent },
                AfterState: new { NewParentId = request.NewParentId },
                Reason: "catalog.category.reparent"),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context,
        Guid id,
        CatalogDbContext dbContext,
        CategoryTreeService categoryTree,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return Results.NoContent();
        }

        var hasChildren = await dbContext.Categories.AnyAsync(c => c.ParentId == id, cancellationToken);
        if (hasChildren)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.category.has_children",
                "Category has children",
                "The category still has child categories; remove them first.");
        }

        var hasProducts = await dbContext.ProductCategories.AnyAsync(pc => pc.CategoryId == id, cancellationToken);
        if (hasProducts)
        {
            return AdminCatalogResponseFactory.Problem(
                context,
                StatusCodes.Status409Conflict,
                "catalog.category.in_use",
                "Category is referenced by products",
                "Reassign or remove the referencing products before deleting.");
        }

        await categoryTree.DetachAsync(dbContext, id, cancellationToken);
        category.DeletedAt = DateTimeOffset.UtcNow;
        category.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: AdminCatalogResponseFactory.ResolveActorAccountId(context),
                ActorRole: "admin",
                Action: "catalog.category.deleted",
                EntityType: nameof(Category),
                EntityId: id,
                BeforeState: new { category.Slug },
                AfterState: null,
                Reason: "catalog.category.delete"),
            cancellationToken);

        return Results.NoContent();
    }
}

public sealed record CreateCategoryRequest(Guid? ParentId, string Slug, string NameAr, string NameEn, int? DisplayOrder);
public sealed record UpdateCategoryRequest(string? NameAr, string? NameEn, int? DisplayOrder, bool? IsActive);
public sealed record ReparentCategoryRequest(Guid? NewParentId);

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.NameEn).MaximumLength(200).When(x => x.NameEn is not null);
    }
}
