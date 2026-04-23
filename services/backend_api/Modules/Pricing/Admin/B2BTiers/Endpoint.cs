using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.B2BTiers;

public sealed record CreateB2BTierRequest(string Slug, string Name, int DefaultDiscountBps);
public sealed record UpdateB2BTierRequest(string Name, int DefaultDiscountBps, bool IsActive);
public sealed record AssignTierRequest(string TierSlug);
public sealed record B2BTierDto(Guid Id, string Slug, string Name, int DefaultDiscountBps, bool IsActive);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapB2BTierEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/b2b-tiers");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("", ListAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tier.read");
        group.MapPost("", CreateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tier.write");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tier.write");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(adminAuth).RequirePermission("pricing.tier.write");

        builder.MapPost("/accounts/{accountId:guid}/tier", AssignTierAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("pricing.tier.write");
        return builder;
    }

    private static async Task<IResult> ListAsync(PricingDbContext db, CancellationToken ct)
    {
        var rows = await db.B2BTiers.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(ct);
        return Results.Ok(rows.Select(r => new B2BTierDto(r.Id, r.Slug, r.Name, r.DefaultDiscountBps, r.IsActive)));
    }

    private static async Task<IResult> CreateAsync(CreateB2BTierRequest request, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Name))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tier.invalid", "Slug and name required", "");
        }
        if (request.DefaultDiscountBps < 0 || request.DefaultDiscountBps > 10_000)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tier.invalid", "DefaultDiscountBps must be 0–10000", "");
        }
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.B2BTiers.AnyAsync(t => t.Slug == slug, ct))
        {
            return AdminPricingResponseFactory.Problem(context, 409, "pricing.tier.duplicate_slug", "Tier slug exists", "");
        }
        var entity = new B2BTier
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = request.Name,
            DefaultDiscountBps = request.DefaultDiscountBps,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.B2BTiers.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return AdminPricingResponseFactory.Problem(context, 409, "pricing.tier.duplicate_slug", "Tier slug exists", "");
        }

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.b2b_tier.created", nameof(B2BTier), entity.Id,
            null, new { entity.Slug, entity.Name, entity.DefaultDiscountBps },
            "pricing.b2b_tier.create"), ct);

        return Results.Created($"/v1/admin/pricing/b2b-tiers/{entity.Id:N}",
            new B2BTierDto(entity.Id, entity.Slug, entity.Name, entity.DefaultDiscountBps, entity.IsActive));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateB2BTierRequest request, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tier.invalid", "Name required", "");
        }
        if (request.DefaultDiscountBps < 0 || request.DefaultDiscountBps > 10_000)
        {
            return AdminPricingResponseFactory.Problem(context, 400, "pricing.tier.invalid", "DefaultDiscountBps must be 0–10000", "");
        }
        var entity = await db.B2BTiers.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.tier.not_found", "Not found", "");
        }
        var before = new { entity.Name, entity.DefaultDiscountBps, entity.IsActive };
        entity.Name = request.Name;
        entity.DefaultDiscountBps = request.DefaultDiscountBps;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.b2b_tier.updated", nameof(B2BTier), entity.Id,
            before, new { entity.Name, entity.DefaultDiscountBps, entity.IsActive },
            "pricing.b2b_tier.update"), ct);

        return Results.Ok(new B2BTierDto(entity.Id, entity.Slug, entity.Name, entity.DefaultDiscountBps, entity.IsActive));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, CancellationToken ct)
    {
        var entity = await db.B2BTiers.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.tier.not_found", "Not found", "");
        }
        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.b2b_tier.deactivated", nameof(B2BTier), entity.Id,
            null, new { entity.Id, entity.IsActive }, "pricing.b2b_tier.delete"), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> AssignTierAsync(Guid accountId, AssignTierRequest request, HttpContext context, PricingDbContext db, IAuditEventPublisher audit, CancellationToken ct)
    {
        var slug = request.TierSlug.Trim().ToLowerInvariant();
        var tier = await db.B2BTiers.SingleOrDefaultAsync(t => t.Slug == slug && t.IsActive, ct);
        if (tier is null)
        {
            return AdminPricingResponseFactory.Problem(context, 404, "pricing.tier.not_found", "Tier not found", "");
        }

        var existing = await db.AccountB2BTiers.SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        object? before = existing is null ? null : new { existing.TierId };
        if (existing is null)
        {
            db.AccountB2BTiers.Add(new AccountB2BTier
            {
                AccountId = accountId,
                TierId = tier.Id,
                AssignedAt = DateTimeOffset.UtcNow,
                AssignedByAccountId = AdminPricingResponseFactory.ResolveActorAccountId(context),
            });
        }
        else
        {
            existing.TierId = tier.Id;
            existing.AssignedAt = DateTimeOffset.UtcNow;
            existing.AssignedByAccountId = AdminPricingResponseFactory.ResolveActorAccountId(context);
        }
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            AdminPricingResponseFactory.ResolveActorAccountId(context),
            "admin", "pricing.account_b2b_tier.assigned", nameof(AccountB2BTier), accountId,
            before, new { TierId = tier.Id, tier.Slug },
            "pricing.tier.assign"), ct);

        return Results.NoContent();
    }
}
