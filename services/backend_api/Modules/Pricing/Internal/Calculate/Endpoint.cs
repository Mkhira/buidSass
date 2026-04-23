using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Pricing.Internal.Calculate;

public static class Endpoint
{
    // TODO (spec 011): migrate from Admin JWT to service-to-service signed JWT.
    public static IEndpointRouteBuilder MapCalculateEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/calculate", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("pricing.internal.calculate");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CalculateRequest request,
        HttpContext context,
        IPriceCalculator calculator,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        CancellationToken cancellationToken)
    {
        var result = await CalculateHandler.HandleAsync(
            request,
            calculator,
            catalogDb,
            pricingDb,
            nowUtc: DateTimeOffset.UtcNow,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminPricingResponseFactory.Problem(
                context, result.StatusCode, result.ReasonCode!, ResolveTitle(result.ReasonCode!), result.Detail!);
        }

        return Results.Ok(result.Response);
    }

    private static string ResolveTitle(string reasonCode) => reasonCode switch
    {
        "pricing.product.not_found" => "Product not found",
        "pricing.coupon.invalid" => "Invalid coupon",
        "pricing.coupon.expired" => "Coupon expired",
        "pricing.coupon.limit_reached" => "Coupon limit reached",
        "pricing.coupon.excludes_restricted" => "Coupon excludes restricted products",
        "pricing.currency_mismatch" => "Currency mismatch",
        "pricing.tax_rate_missing" => "Tax rate missing",
        "pricing.lines_required" => "Lines required",
        "pricing.invalid_qty" => "Invalid quantity",
        "pricing.product.no_price" => "Product has no price",
        "pricing.invalid_mode" => "Invalid mode",
        _ => "Pricing error",
    };
}
