using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.ListMyVerifications;

public static class ListMyVerificationsEndpoint
{
    public static IEndpointRouteBuilder MapListMyVerificationsEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListMyVerificationsHandler handler,
        CancellationToken ct,
        int page = 1,
        int page_size = 25)
    {
        var customerId = VerificationResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return VerificationResponseFactory.Problem(
                context, 401,
                VerificationReasonCode.AccountInactive,
                "Authentication required.");
        }

        var result = await handler.HandleAsync(
            customerId.Value,
            new ListMyVerificationsQuery(page, page_size),
            ct);
        return Results.Ok(result);
    }
}
