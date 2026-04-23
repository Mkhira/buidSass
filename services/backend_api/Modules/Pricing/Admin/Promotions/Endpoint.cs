using System.Text.Json;
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
        var validation = ValidatePromotion(context, request.Kind, request.Name, request.ConfigJson, request.MarketCodes, request.StartsAt, request.EndsAt);
        if (validation.Problem is not null) return validation.Problem;

        var entity = new Promotion
        {
            Id = Guid.NewGuid(),
            Kind = validation.Kind!,
            Name = request.Name,
            ConfigJson = validation.ConfigJson!,
            AppliesToProductIds = request.AppliesToProductIds,
            AppliesToCategoryIds = request.AppliesToCategoryIds,
            MarketCodes = validation.MarketCodes!,
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
            new { entity.Kind, entity.Name, entity.Priority, entity.MarketCodes, entity.AppliesToProductIds, entity.AppliesToCategoryIds, entity.StartsAt, entity.EndsAt },
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
        // Update keeps the existing kind; callers cannot switch kinds in place.
        var validation = ValidatePromotion(context, entity.Kind, request.Name, request.ConfigJson, request.MarketCodes, request.StartsAt, request.EndsAt);
        if (validation.Problem is not null) return validation.Problem;

        var before = new
        {
            entity.Name,
            entity.ConfigJson,
            entity.AppliesToProductIds,
            entity.AppliesToCategoryIds,
            entity.MarketCodes,
            entity.Priority,
            entity.StartsAt,
            entity.EndsAt,
        };
        entity.Name = request.Name;
        entity.ConfigJson = validation.ConfigJson!;
        entity.AppliesToProductIds = request.AppliesToProductIds;
        entity.AppliesToCategoryIds = request.AppliesToCategoryIds;
        entity.MarketCodes = validation.MarketCodes!;
        entity.Priority = request.Priority;
        entity.StartsAt = request.StartsAt;
        entity.EndsAt = request.EndsAt;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.promotion.updated", nameof(Promotion), entity.Id,
            before,
            new
            {
                entity.Name,
                entity.ConfigJson,
                entity.AppliesToProductIds,
                entity.AppliesToCategoryIds,
                entity.MarketCodes,
                entity.Priority,
                entity.StartsAt,
                entity.EndsAt,
            },
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
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.promotion.deleted", nameof(Promotion), entity.Id,
            null, new { entity.Id, entity.DeletedAt }, "pricing.promotion.delete"), ct);

        cache.Invalidate();
        return Results.NoContent();
    }

    private readonly record struct PromotionValidation(IResult? Problem, string? Kind, string? ConfigJson, string[]? MarketCodes);

    private static PromotionValidation ValidatePromotion(
        HttpContext context,
        string? rawKind,
        string? name,
        string? configJson,
        string[]? marketCodes,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt)
    {
        var kind = rawKind?.Trim().ToLowerInvariant();
        if (kind is not ("percent_off" or "amount_off" or "bogo" or "bundle_wrapper"))
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid_kind", "Invalid promotion kind", "kind must be one of: percent_off, amount_off, bogo, bundle_wrapper"), null, null, null);
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "Name required", ""), null, null, null);
        }
        if (marketCodes is null)
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "At least one marketCode required", ""), null, null, null);
        }
        var markets = marketCodes
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (markets.Length == 0)
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "At least one non-blank marketCode required", ""), null, null, null);
        }
        if (startsAt is { } s && endsAt is { } e && e <= s)
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid_window", "Invalid window", "endsAt must be after startsAt"), null, null, null);
        }

        var normalizedConfig = string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson;
        try
        {
            using var _ = JsonDocument.Parse(normalizedConfig);
        }
        catch (JsonException)
        {
            return new(AdminPricingResponseFactory.Problem(context, 400, "pricing.promotion.invalid", "configJson must be valid JSON", ""), null, null, null);
        }

        return new(null, kind, normalizedConfig, markets);
    }
}
