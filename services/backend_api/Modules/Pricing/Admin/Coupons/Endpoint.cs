using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Pricing.Admin.Coupons;

public sealed record CreateCouponRequest(
    string Code,
    string Kind,
    int Value,
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    bool ExcludesRestricted,
    string[] MarketCodes,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);

public sealed record UpdateCouponRequest(
    long? CapMinor,
    int? PerCustomerLimit,
    int? OverallLimit,
    bool ExcludesRestricted,
    string[] MarketCodes,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);

public sealed record CouponDto(Guid Id, string Code, string Kind, int Value, bool IsActive, int UsedCount);
public sealed record CouponRedemptionDto(Guid Id, Guid AccountId, Guid? OrderId, DateTimeOffset RedeemedAt);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCouponEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/coupons");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.coupon.read");
        group.MapPost("", CreateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.coupon.write");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.coupon.write");
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.coupon.write");
        group.MapGet("/{id:guid}/redemptions", ListRedemptionsAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.coupon.read");
        return builder;
    }

    private static async Task<IResult> ListAsync(PricingDbContext db, CancellationToken ct)
    {
        var rows = await db.Coupons.AsNoTracking().Where(c => c.DeletedAt == null).ToListAsync(ct);
        return Results.Ok(rows.Select(r => new CouponDto(r.Id, r.Code, r.Kind, r.Value, r.IsActive, r.UsedCount)));
    }

    private static async Task<IResult> CreateAsync(
        CreateCouponRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "Code required", "");
        }
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("percent" or "amount"))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "Kind must be 'percent' or 'amount'", "");
        }
        if (request.Value < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "Value must be >= 0", "");
        }
        if (kind == "percent" && request.Value > 10_000)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "Percent value must be 0–10000 basis points", "");
        }
        if (request.CapMinor is long cap && cap < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "CapMinor must be >= 0", "");
        }
        if (request.MarketCodes is null || request.MarketCodes.Length == 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "At least one marketCode required", "");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct))
        {
            return AdminPricingResponseFactory.Problem(context, 409, "pricing.coupon.duplicate_code", "Coupon code exists", "");
        }

        var entity = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = code,
            Kind = kind,
            Value = request.Value,
            CapMinor = request.CapMinor,
            PerCustomerLimit = request.PerCustomerLimit,
            OverallLimit = request.OverallLimit,
            ExcludesRestricted = request.ExcludesRestricted,
            MarketCodes = request.MarketCodes.Select(m => m.Trim().ToLowerInvariant()).ToArray(),
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Coupons.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent create with the same code lost the race on IX_coupons_Code.
            return AdminPricingResponseFactory.Problem(context, 409, "pricing.coupon.duplicate_code", "Coupon code exists", "");
        }

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.coupon.created", nameof(Coupon), entity.Id,
            null, new { entity.Code, entity.Kind, entity.Value, entity.CapMinor, entity.OverallLimit, entity.PerCustomerLimit, entity.MarketCodes, entity.ExcludesRestricted, entity.ValidFrom, entity.ValidTo },
            "pricing.coupon.create"), ct);

        return Results.Created($"/v1/admin/pricing/coupons/{entity.Id:N}",
            new CouponDto(entity.Id, entity.Code, entity.Kind, entity.Value, entity.IsActive, entity.UsedCount));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCouponRequest request,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        CancellationToken ct)
    {
        if (request.CapMinor is long cap && cap < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "CapMinor must be >= 0", "");
        }
        if (request.PerCustomerLimit is int pcl && pcl < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "PerCustomerLimit must be >= 0", "");
        }
        if (request.OverallLimit is int ol && ol < 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "OverallLimit must be >= 0", "");
        }
        if (request.MarketCodes is null)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "At least one marketCode required", "");
        }
        var markets = request.MarketCodes
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (markets.Length == 0)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.coupon.invalid", "At least one non-blank marketCode required", "");
        }

        var entity = await db.Coupons.SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.coupon.not_found", "Not found", "");
        }
        var before = new { entity.CapMinor, entity.PerCustomerLimit, entity.OverallLimit, entity.ExcludesRestricted, entity.MarketCodes, entity.ValidFrom, entity.ValidTo };
        entity.CapMinor = request.CapMinor;
        entity.PerCustomerLimit = request.PerCustomerLimit;
        entity.OverallLimit = request.OverallLimit;
        entity.ExcludesRestricted = request.ExcludesRestricted;
        entity.MarketCodes = markets;
        entity.ValidFrom = request.ValidFrom;
        entity.ValidTo = request.ValidTo;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.coupon.updated", nameof(Coupon), entity.Id,
            before, new { entity.CapMinor, entity.PerCustomerLimit, entity.OverallLimit, entity.ExcludesRestricted, entity.MarketCodes, entity.ValidFrom, entity.ValidTo },
            "pricing.coupon.update"), ct);

        return Results.Ok(new CouponDto(entity.Id, entity.Code, entity.Kind, entity.Value, entity.IsActive, entity.UsedCount));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        HttpContext context,
        PricingDbContext db,
        IAuditEventPublisher audit,
        CancellationToken ct)
    {
        var entity = await db.Coupons.SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.coupon.not_found", "Not found", "");
        }
        var before = new { entity.IsActive };
        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.coupon.deactivated", nameof(Coupon), entity.Id,
            before, new { entity.IsActive }, "pricing.coupon.deactivate"), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListRedemptionsAsync(
        Guid id,
        HttpContext context,
        int? page,
        int? pageSize,
        PricingDbContext db,
        CancellationToken ct)
    {
        var p = page is null or < 1 ? 1 : page.Value;
        var ps = pageSize is null or < 1 or > 100 ? 20 : pageSize.Value;
        var rows = await db.CouponRedemptions.AsNoTracking()
            .Where(r => r.CouponId == id)
            .OrderByDescending(r => r.RedeemedAt)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(r => new CouponRedemptionDto(r.Id, r.AccountId, r.OrderId, r.RedeemedAt)));
    }
}
