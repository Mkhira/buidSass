using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.ResubmitWithInfo;

public static class ResubmitWithInfoEndpoint
{
    public static IEndpointRouteBuilder MapResubmitWithInfoEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/resubmit", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ResubmitWithInfoRequest? body,
        HttpContext context,
        ResubmitWithInfoHandler handler,
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

        if (body is null)
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.RequiredFieldMissing,
                "Body is required.");
        }

        if (string.IsNullOrWhiteSpace(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return VerificationResponseFactory.Problem(
                context, 400,
                VerificationReasonCode.IdempotencyKeyMissing,
                "Idempotency-Key header is required for this endpoint.");
        }

        var result = await handler.HandleAsync(customerId.Value, id, body, ct);
        if (result.IsNotFound)
        {
            return VerificationResponseFactory.Problem(
                context, 404,
                "verification.not_found",
                "Verification not found.");
        }
        if (!result.IsSuccess)
        {
            var status = result.ReasonCode switch
            {
                VerificationReasonCode.InvalidStateForAction => 409,
                VerificationReasonCode.OptimisticConcurrencyConflict => 409,
                _ => 400,
            };
            return VerificationResponseFactory.Problem(
                context, status, result.ReasonCode!.Value,
                "Resubmit failed.", result.Detail);
        }
        return Results.Ok(result.Response);
    }
}
