using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.Refunds.Retry;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminRefundRetryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{refundId:guid}/retry", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.refund.write");
        return builder;
    }

    /// <summary>FR-021. Manual retry trigger. The actual retry happens via the
    /// <c>RefundRetryWorker</c>; this endpoint just resets <c>NextRetryAt</c> so the worker
    /// picks the row up immediately.</summary>
    private static async Task<IResult> HandleAsync(
        Guid refundId,
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
        var refund = await db.Refunds.FirstOrDefaultAsync(rf => rf.Id == refundId, ct);
        if (refund is null)
        {
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Refund not found.");
        }
        if (!string.Equals(refund.State, RefundStateMachine.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"retry only valid from state failed (current: {refund.State}).");
        }
        var nowUtc = DateTimeOffset.UtcNow;
        refund.NextRetryAt = nowUtc;
        refund.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(ct);

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.refund.retry_requested",
            refund.ReturnRequestId, new { refundId = refund.Id }, new { nextRetryAt = nowUtc }, null, ct);

        return Results.Ok(new
        {
            id = refund.Id,
            state = refund.State,
            nextRetryAt = nowUtc,
            queuedForRetry = true,
        });
    }
}
