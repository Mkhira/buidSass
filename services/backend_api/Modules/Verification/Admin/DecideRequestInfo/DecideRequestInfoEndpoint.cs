using BackendApi.Modules.Verification.Authorization;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Admin.DecideRequestInfo;

public static class DecideRequestInfoEndpoint
{
    public static IEndpointRouteBuilder MapDecideRequestInfoEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/request-info", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        DecideRequestInfoRequest? body,
        HttpContext context,
        DecideRequestInfoHandler handler,
        CancellationToken ct)
    {
        if (!context.User.HasClaim("permission", VerificationPermissions.Review)
         && !context.User.HasClaim("permissions", VerificationPermissions.Review))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 403,
                "verification.review_permission_required",
                "verification.review permission required.");
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

        var (ok, reason, detail) = DecideRequestInfoValidator.Validate(body);
        if (!ok)
        {
            return AdminVerificationResponseFactory.Problem(
                context, 400, reason!.Value, "Request-info validation failed.", detail);
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
                context, status, result.ReasonCode!.Value, "Request-info failed.", result.Detail);
        }

        return Results.Ok(result.Response);
    }
}
