using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Promotions;

public sealed record CreatePromotionRequest(
    string Kind,
    string Name,
    string ConfigJson,
    Guid[]? AppliesToProductIds,
    Guid[]? AppliesToCategoryIds,
    string[] MarketCodes,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record UpdatePromotionRequest(
    string Name,
    string ConfigJson,
    Guid[]? AppliesToProductIds,
    Guid[]? AppliesToCategoryIds,
    string[] MarketCodes,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record PromotionDto(Guid Id, string Kind, string Name, int Priority, bool IsActive);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapPromotionEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/promotions");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.read");
        group.MapPost("", CreateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.write");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.write");
        group.MapPost("/{id:guid}/activate", ActivateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.write");
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.write");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.promotion.write");
        return builder;
    }

    private static async Task<IResult> ListAsync(PricingDbContext db, CancellationToken ct)
    {
        var rows = await db.Promotions.AsNoTracking().Where(p => p.DeletedAt == null).ToListAsync(ct);
        return Results.Ok(rows.Select(r => new PromotionDto(r.Id, r.Kind, r.Name, r.Priority, r.IsActive)));
    }

    private static async Task<IResult> CreateAsync(
        CreatePromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        PromotionCache cache,
        CancellationToken ct)
    {
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("percent_off" or "amount_off" or "bogo" or "bundle_wrapper"))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid_kind", "Invalid promotion kind", "kind must be one of: percent_off, amount_off, bogo, bundle_wrapper");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "Name required", "");
        }
        if (request.MarketCodes is null || request.MarketCodes.Length == 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "At least one marketCode required", "");
        }

        var entity = new Promotion
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = request.Name,
            ConfigJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson,
            AppliesToProductIds = request.AppliesToProductIds,
            AppliesToCategoryIds = request.AppliesToCategoryIds,
            MarketCodes = request.MarketCodes.Select(m => m.Trim().ToLowerInvariant()).ToArray(),
            Priority = request.Priority,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Promotions.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin",
            "pricing.promotion.created",
            nameof(Promotion),
            entity.Id,
            null,
            new { entity.Kind, entity.Name, entity.Priority, entity.MarketCodes },
            "pricing.promotion.create"), ct);

        cache.Invalidate();
        return Results.Created($"/v1/admin/pricing/promotions/{entity.Id:N}", new PromotionDto(entity.Id, entity.Kind, entity.Name, entity.Priority, entity.IsActive));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdatePromotionRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        PromotionCache cache,
        CancellationToken ct)
    {
        var entity = await db.Promotions.SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.promotion.not_found", "Not found", "");
        }
        var before = new { entity.Name, entity.ConfigJson, entity.Priority, entity.StartsAt, entity.EndsAt };
        entity.Name = request.Name;
        entity.ConfigJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson;
        entity.AppliesToProductIds = request.AppliesToProductIds;
        entity.AppliesToCategoryIds = request.AppliesToCategoryIds;
        entity.MarketCodes = request.MarketCodes.Select(m => m.Trim().ToLowerInvariant()).ToArray();
        entity.Priority = request.Priority;
        entity.StartsAt = request.StartsAt;
        entity.EndsAt = request.EndsAt;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.promotion.updated", nameof(Promotion), entity.Id,
            before, new { entity.Name, entity.ConfigJson, entity.Priority, entity.StartsAt, entity.EndsAt },
            "pricing.promotion.update"), ct);

        cache.Invalidate();
        return Results.Ok(new PromotionDto(entity.Id, entity.Kind, entity.Name, entity.Priority, entity.IsActive));
    }

    private static async Task<IResult> ActivateAsync(Guid id, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, PromotionCache cache, CancellationToken ct)
        => await ToggleAsync(id, true, "pricing.promotion.activated", context, db, audit, cache, ct);

    private static async Task<IResult> DeactivateAsync(Guid id, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, PromotionCache cache, CancellationToken ct)
        => await ToggleAsync(id, false, "pricing.promotion.deactivated", context, db, audit, cache, ct);

    private static async Task<IResult> ToggleAsync(
        Guid id, bool isActive, string action,
        HttpContext context, PricingDbContext db, IAuditEventPublisher audit, PromotionCache cache, CancellationToken ct)
    {
        var entity = await db.Promotions.SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.promotion.not_found", "Not found", "");
        }
        var before = new { entity.IsActive };
        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", action, nameof(Promotion), entity.Id,
            before, new { entity.IsActive }, "pricing.promotion.toggle"), ct);

        cache.Invalidate();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, PromotionCache cache, CancellationToken ct)
    {
        var entity = await db.Promotions.SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.promotion.not_found", "Not found", "");
        }
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.IsActive = false;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.promotion.deleted", nameof(Promotion), entity.Id,
            null, new { entity.Id, entity.DeletedAt }, "pricing.promotion.delete"), ct);

        cache.Invalidate();
        return Results.NoContent();
    }
}
