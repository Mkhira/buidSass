using BackendApi.Modules.Verification.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Customer.GetMarketSchema;

/// <summary>
/// HTTP surface for <see cref="GetMarketSchemaHandler"/>. Returns the active
/// per-market schema for the authenticated customer. Spec 020 task T100.
/// </summary>
public static class GetMarketSchemaEndpoint
{
    public static IEndpointRouteBuilder MapGetMarketSchemaEndpoint(
        this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/schema", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        GetMarketSchemaHandler handler,
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

        var marketCode = VerificationResponseFactory.ResolveMarketCode(context);
        var schema = await handler.HandleAsync(marketCode, ct);
        if (schema is null)
        {
            return VerificationResponseFactory.Problem(
                context, 404,
                VerificationReasonCode.MarketUnsupported,
                $"No active verification schema is configured for market '{marketCode}'.");
        }
        return Results.Ok(schema);
    }
}
