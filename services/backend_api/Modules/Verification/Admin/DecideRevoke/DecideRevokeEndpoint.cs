using BackendApi.Modules.Verification.Authorization;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Admin.DecideRevoke;

public static class DecideRevokeEndpoint
{
    public static IEndpointRouteBuilder MapDecideRevokeEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/revoke", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        DecideRevokeRequest? body,
        HttpContext context,
        DecideRevokeHandler handler,
        CancellationToken ct)
    {
        // Revoke uses a DISTINCT permission per spec 020 contracts §3.6 — review
        // is not enough; a reviewer needs explicit revoke permission.
        if (!context.User.HasClaim("permission", VerificationPermissions.Revoke)
         && !context.User.HasClaim("permissions", VerificationPermissions.Revoke))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 403,
                "verification.revoke_permission_required",
                "verification.revoke permission required.");
        }

        var reviewerId = AdminVerificationResponseFactory.ResolveReviewerId(context);
        if (reviewerId is null)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 401,
                VerificationReasonCode.AccountInactive,
                "Reviewer authentication required.");
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        var (ok, reason, detail) = DecideRevokeValidator.Validate(body);
        if (!ok)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 400, reason!.Value, "Revoke validation failed.", detail);
        }

        var reviewerMarkets = AdminVerificationResponseFactory.ResolveAssignedMarkets(context);
        var result = await handler.HandleAsync(id, reviewerId.Value, reviewerMarkets, body!, ct);
        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                VerificationReasonCode.AlreadyDecided => 409,
                VerificationReasonCode.InvalidStateForAction => 409,
                _ => 400,
            };
            return AdminVerificationResponseFactory.Problem(
                context, status, result.ReasonCode!.Value, "Revoke failed.", result.Detail);
        }

        return Results.Ok(result.Response);
    }
}
