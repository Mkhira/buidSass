using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Caches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.TaxRates;

public sealed record CreateTaxRateRequest(string MarketCode, string Kind, int RateBps, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);
public sealed record PatchTaxRateRequest(DateTimeOffset EffectiveTo);
public sealed record TaxRateDto(Guid Id, string MarketCode, string Kind, int RateBps, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapTaxRateEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/tax-rates");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tax.read");
        group.MapPost("", CreateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tax.write");
        group.MapPatch("/{id:guid}", PatchAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tax.write");
        return builder;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        string? market,
        PricingDbContext db,
        CancellationToken ct)
    {
        var query = db.TaxRates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(market))
        {
            var m = market.Trim().ToLowerInvariant();
            query = query.Where(r => r.MarketCode == m);
        }
        var rows = await query.OrderBy(r => r.MarketCode).ThenByDescending(r => r.EffectiveFrom).ToListAsync(ct);
        return Results.Ok(rows.Select(r => new TaxRateDto(r.Id, r.MarketCode, r.Kind, r.RateBps, r.EffectiveFrom, r.EffectiveTo)));
    }

    private static async Task<IResult> CreateAsync(
        CreateTaxRateRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        TaxRateCache cache,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MarketCode) || string.IsNullOrWhiteSpace(request.Kind))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tax.invalid_rate", "marketCode and kind required", "");
        }
        if (request.RateBps < 0 || request.RateBps > 100_00)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tax.invalid_rate", "Invalid rate", "RateBps must be between 0 and 10000.");
        }
        var market = request.MarketCode.Trim().ToLowerInvariant();
        var kind = request.Kind.Trim().ToLowerInvariant();

        var entity = new TaxRate
        {
            Id = Guid.NewGuid(),
            MarketCode = market,
            Kind = kind,
            RateBps = request.RateBps,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            CreatedByAccountId = AdminPricingResponseFactory.ResolveActorAccountId(context),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.TaxRates.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: AdminPricingResponseFactory.ResolveActorAccountId(context),
            ActorRole: "admin",
            Action: "pricing.tax_rate.created",
            EntityType: nameof(TaxRate),
            EntityId: entity.Id,
            BeforeState: null,
            AfterState: new { entity.MarketCode, entity.Kind, entity.RateBps, entity.EffectiveFrom, entity.EffectiveTo },
            Reason: "pricing.tax_rate.create"), ct);

        cache.Invalidate(market, kind);
        return Results.Created($"/v1/admin/pricing/tax-rates/{entity.Id:N}", new TaxRateDto(entity.Id, entity.MarketCode, entity.Kind, entity.RateBps, entity.EffectiveFrom, entity.EffectiveTo));
    }

    private static async Task<IResult> PatchAsync(
        Guid id,
        PatchTaxRateRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        TaxRateCache cache,
        CancellationToken ct)
    {
        var entity = await db.TaxRates.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.tax.not_found", "Tax rate not found", "");
        }

        var before = new { entity.EffectiveTo };
        entity.EffectiveTo = request.EffectiveTo;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: AdminPricingResponseFactory.ResolveActorAccountId(context),
            ActorRole: "admin",
            Action: "pricing.tax_rate.patched",
            EntityType: nameof(TaxRate),
            EntityId: entity.Id,
            BeforeState: before,
            AfterState: new { entity.EffectiveTo },
            Reason: "pricing.tax_rate.patch"), ct);

        cache.Invalidate(entity.MarketCode, entity.Kind);
        return Results.Ok(new TaxRateDto(entity.Id, entity.MarketCode, entity.Kind, entity.RateBps, entity.EffectiveFrom, entity.EffectiveTo));
    }
}
