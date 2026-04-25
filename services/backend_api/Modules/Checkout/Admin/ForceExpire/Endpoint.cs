using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Checkout.Admin.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Admin.ForceExpire;

public sealed record ForceExpireRequest(string? Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminForceExpireEndpoint(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapPost("/sessions/{sessionId:guid}/expire", HandleAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("checkout.write");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid sessionId,
        ForceExpireRequest? request,
        HttpContext context,
        CheckoutDbContext db,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken ct)
    {
        var actorId = AdminCheckoutResponseFactory.ResolveActorAccountId(context);
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return AdminCheckoutResponseFactory.Problem(context, 404, "checkout.session.not_found", "Session not found", "");
        }
        if (session.State is CheckoutStates.Confirmed or CheckoutStates.Expired)
        {
            return AdminCheckoutResponseFactory.Problem(context, 409, "checkout.invalid_state", "Session already terminal", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (!CheckoutStates.TryTransition(session, CheckoutStates.Expired, nowUtc))
        {
            return AdminCheckoutResponseFactory.Problem(context, 409, "checkout.invalid_state", "Cannot expire from current state", "");
        }
        session.FailureReasonCode = request?.Reason ?? "checkout.admin_expired";
        await db.SaveChangesAsync(ct);

        // SC-009: audit row with actor + reason.
        await auditEventPublisher.PublishAsync(new AuditEvent(
            actorId,
            "admin",
            "checkout.admin_expired",
            nameof(Entities.CheckoutSession),
            session.Id,
            null,
            new { sessionId = session.Id, reason = session.FailureReasonCode, session.CartId, session.AccountId },
            "checkout.admin.expire"), ct);

        return Results.Ok(new { sessionId = session.Id, state = session.State, expiredAt = session.ExpiredAt });
    }
}
