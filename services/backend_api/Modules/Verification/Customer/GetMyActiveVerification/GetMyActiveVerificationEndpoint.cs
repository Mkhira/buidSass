using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.GetMyActiveVerification;

public static class GetMyActiveVerificationEndpoint
{
    public static IEndpointRouteBuilder MapGetMyActiveVerificationEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/active", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        GetMyActiveVerificationHandler handler,
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

        var result = await handler.HandleAsync(customerId.Value, ct);
        // Per contracts §2.2, returning null is an explicit semantic — customer
        // has no active verification — so we return 200 with null body rather
        // than 404. Spec 014 / customer UI uses null to render the "you haven't
        // verified yet" empty state.
        return Results.Ok(result);
    }
}
