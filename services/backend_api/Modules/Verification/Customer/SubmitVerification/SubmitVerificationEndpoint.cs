using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// HTTP surface for <see cref="SubmitVerificationHandler"/>. Wired by
/// <see cref="VerificationModule.MapVerificationEndpoints"/> at module bootstrap.
/// Requires <c>Idempotency-Key</c> header per spec 020 contracts §2.1.
/// </summary>
public static class SubmitVerificationEndpoint
{
    public static IEndpointRouteBuilder MapSubmitVerificationEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SubmitVerificationRequest? body,
        HttpContext context,
        SubmitVerificationHandler handler,
        CancellationToken ct)
    {
        var customerId = VerificationResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return VerificationResponseFactory.Problem(
                context, 401,
                Primitives.VerificationReasonCode.AccountInactive,
                "Authentication required.");
        }

        var (ok, reason, detail) = SubmitVerificationValidator.Validate(body);
        if (!ok)
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                reason!.Value,
                "Submission validation failed.",
                detail);
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                Primitives.VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        var marketCode = VerificationResponseFactory.ResolveMarketCode(context);
        var result = await handler.HandleAsync(customerId.Value, marketCode, body!, ct);

        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                Primitives.VerificationReasonCode.AlreadyPending => 409,
                Primitives.VerificationReasonCode.MarketUnsupported => 400,
                _ => 400,
            };
            return VerificationResponseFactory.Problem(
                context, status,
                result.ReasonCode!.Value,
                "Submission rejected.",
                result.Detail);
        }

        return Results.Created(
            $"/api/customer/verifications/{result.Response!.Id}",
            result.Response);
    }
}
