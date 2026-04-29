using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.RequestRenewal;

public static class RequestRenewalEndpoint
{
    public static IEndpointRouteBuilder MapRequestRenewalEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/renew", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        RequestRenewalRequest? body,
        HttpContext context,
        RequestRenewalHandler handler,
        CancellationToken ct)
    {
        var customerId = VerificationResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return VerificationResponseFactory.Problem(
                context, 401,
                VerificationReasonCode.AccountInactive,
                "Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        // Empty body is fine — both fields are optional.
        var request = body ?? new RequestRenewalRequest(null, null);

        var marketCode = VerificationResponseFactory.ResolveMarketCode(context);
        var result = await handler.HandleAsync(customerId.Value, marketCode, request, ct);
        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                VerificationReasonCode.RenewalNotEligible => 409,
                VerificationReasonCode.RenewalAlreadyPending => 409,
                VerificationReasonCode.MarketUnsupported => 400,
                _ => 400,
            };
            return VerificationResponseFactory.Problem(
                context, status, result.ReasonCode!.Value,
                "Renewal request failed.", result.Detail);
        }
        return Results.Created(
            $"/api/customer/verifications/{result.Response!.Id}",
            result.Response);
    }
}
