using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Pricing.Customer.Common;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Pricing.Customer.PriceCart;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapPriceCartEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/price-cart", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        PriceCartRequest request,
        HttpContext context,
        IPriceCalculator calculator,
        CatalogDbContext catalogDb,
        PricingDbContext pricingDb,
        CancellationToken cancellationToken)
    {
        Guid? accountId = null;
        var sub = context.User.FindFirst("sub")?.Value;
        if (Guid.TryParse(sub, out var parsed))
        {
            accountId = parsed;
        }

        var result = await PriceCartHandler.HandleAsync(
            request,
            calculator,
            catalogDb,
            pricingDb,
            nowUtc: DateTimeOffset.UtcNow,
            accountId: accountId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return CustomerPricingResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                ResolveTitle(result.ReasonCode!),
                result.Detail!);
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
        _ => "Pricing error",
    };
}
