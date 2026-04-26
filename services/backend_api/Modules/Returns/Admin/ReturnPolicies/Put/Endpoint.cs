using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ReturnPolicies.Put;

public sealed record PutPolicyRequest(
    int ReturnWindowDays,
    int? AutoApproveUnderDays,
    int RestockingFeeBp,
    bool ShippingRefundOnFullOnly);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminPutReturnPolicyEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{market}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.policy.write");
        return builder;
    }

    /// <summary>FR-015. Upsert per-market policy. Audited.</summary>
    private static async Task<IResult> HandleAsync(
        string market,
        PutPolicyRequest body,
        HttpContext context,
        ReturnsDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actorId = ReturnsResponseFactory.ResolveAccountId(context);
        if (actorId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        if (string.IsNullOrWhiteSpace(market) || market.Length > 8)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.policy.invalid_market", "market is invalid.");
        }
        if (body is null || body.ReturnWindowDays < 0 || body.RestockingFeeBp < 0 || body.RestockingFeeBp > 10_000)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.policy.invalid_request",
                "returnWindowDays must be ≥ 0 and restockingFeeBp must be in [0,10000].");
        }
        if (body.AutoApproveUnderDays is { } a && a < 0)
        {
            return ReturnsResponseFactory.Problem(context, 400, "return.policy.invalid_request",
                "autoApproveUnderDays must be ≥ 0.");
        }

        var marketCode = market.Trim().ToUpperInvariant();
        var nowUtc = DateTimeOffset.UtcNow;
        // For audit purposes capture the *before* values without tracking the row.
        var before = await db.ReturnPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MarketCode == marketCode, ct);
        var existing = await db.ReturnPolicies.FirstOrDefaultAsync(p => p.MarketCode == marketCode, ct);
        if (existing is null)
        {
            existing = new ReturnPolicy { MarketCode = marketCode };
            db.ReturnPolicies.Add(existing);
        }
        existing.ReturnWindowDays = body.ReturnWindowDays;
        existing.AutoApproveUnderDays = body.AutoApproveUnderDays;
        existing.RestockingFeeBp = body.RestockingFeeBp;
        existing.ShippingRefundOnFullOnly = body.ShippingRefundOnFullOnly;
        existing.UpdatedByAccountId = actorId;
        existing.UpdatedAt = nowUtc;

        await db.SaveChangesAsync(ct);

        await auditPublisher.PublishAsync(new AuditEvent(
            ActorId: actorId.Value,
            ActorRole: "admin",
            Action: "returns.policy.upsert",
            EntityType: "returns.return_policy",
            EntityId: DeterministicGuid(marketCode),
            BeforeState: before is null ? null : new
            {
                before.MarketCode,
                before.ReturnWindowDays,
                before.AutoApproveUnderDays,
                before.RestockingFeeBp,
                before.ShippingRefundOnFullOnly,
            },
            AfterState: new
            {
                existing.MarketCode,
                existing.ReturnWindowDays,
                existing.AutoApproveUnderDays,
                existing.RestockingFeeBp,
                existing.ShippingRefundOnFullOnly,
            },
            Reason: marketCode), ct);

        return Results.Ok(new
        {
            marketCode = existing.MarketCode,
            returnWindowDays = existing.ReturnWindowDays,
            autoApproveUnderDays = existing.AutoApproveUnderDays,
            restockingFeeBp = existing.RestockingFeeBp,
            shippingRefundOnFullOnly = existing.ShippingRefundOnFullOnly,
            updatedAt = existing.UpdatedAt,
        });
    }

    /// <summary>Stable Guid derived from a string so audit-log queries can filter by market.</summary>
    private static Guid DeterministicGuid(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("returns.policy:" + s));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
