using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Checkout.Primitives;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Session TTL in minutes (FR-004).</summary>
    public int SessionTtlMinutes { get; set; } = 30;

    /// <summary>Warning threshold — client hint for session-about-to-expire banner (R10).</summary>
    public int SessionWarningMinutes { get; set; } = 25;

    /// <summary>Idempotency cache TTL (FR-007 / R3).</summary>
    public int IdempotencyTtlMinutes { get; set; } = 5;

    /// <summary>Shipping-quote validity window (R8).</summary>
    public int ShippingQuoteTtlMinutes { get; set; } = 10;

    /// <summary>Expiry worker tick interval (FR-025).</summary>
    public int ExpiryWorkerIntervalSeconds { get; set; } = 60;
}

internal sealed class CheckoutOptionsValidator : IValidateOptions<CheckoutOptions>
{
    public ValidateOptionsResult Validate(string? name, CheckoutOptions o)
    {
        var failures = new List<string>();
        if (o.SessionTtlMinutes <= 0) failures.Add("Checkout:SessionTtlMinutes must be positive.");
        if (o.SessionWarningMinutes <= 0 || o.SessionWarningMinutes >= o.SessionTtlMinutes)
            failures.Add("Checkout:SessionWarningMinutes must be positive and strictly less than SessionTtlMinutes.");
        if (o.IdempotencyTtlMinutes <= 0) failures.Add("Checkout:IdempotencyTtlMinutes must be positive.");
        if (o.ShippingQuoteTtlMinutes <= 0) failures.Add("Checkout:ShippingQuoteTtlMinutes must be positive.");
        if (o.ExpiryWorkerIntervalSeconds <= 0) failures.Add("Checkout:ExpiryWorkerIntervalSeconds must be positive.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
