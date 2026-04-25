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
        CheckoutAuditEmitter audit,
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

        // SC-009 + FR-015: audit row with admin actor + reason. Routed through the centralised
        // emitter so the action vocabulary stays consistent with customer-side transitions.
        await audit.EmitSessionTransitionAsync(
            session, CheckoutAuditActions.SessionAdminExpired,
            actorAccountId: actorId,
            actorRole: CheckoutAuditEmitter.AdminRole,
            reason: session.FailureReasonCode, ct);

        return Results.Ok(new { sessionId = session.Id, state = session.State, expiredAt = session.ExpiredAt });
    }
}
