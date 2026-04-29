using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.GetMyVerification;

public static class GetMyVerificationEndpoint
{
    public static IEndpointRouteBuilder MapGetMyVerificationEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        GetMyVerificationHandler handler,
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

        var result = await handler.HandleAsync(customerId.Value, id, ct);
        if (result is null)
        {
            return VerificationResponseFactory.Problem(
                context, 404,
                "verification.not_found",
                "Verification not found.");
        }
        return Results.Ok(result);
    }
}
