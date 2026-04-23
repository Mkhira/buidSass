namespace BackendApi.Modules.Pricing.Primitives;

public interface IPriceCalculator
{
    Task<PriceCalculationOutcome> CalculateAsync(PricingContext context, CancellationToken cancellationToken);
}

public sealed record PriceCalculationOutcome(
    bool IsSuccess,
    PriceResult? Result,
    int StatusCode,
    string? ReasonCode,
    string? Detail)
{
    public static PriceCalculationOutcome Success(PriceResult result) =>
        new(true, result, 200, null, null);

    public static PriceCalculationOutcome Fail(int statusCode, string reasonCode, string detail) =>
        new(false, null, statusCode, reasonCode, detail);
}
