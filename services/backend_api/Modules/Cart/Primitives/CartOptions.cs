using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Primitives;

public sealed class CartOptions
{
    public const string SectionName = "Cart";

    /// <summary>
    /// Well-known default secret — kept as a constant so the validator can reject it outside
    /// Development/Test. A leaked or unrotated default in prod lets an attacker forge valid
    /// cart tokens, so this gate is intentionally hard.
    /// </summary>
    public const string DefaultTokenSecret = "dev-only-cart-token-secret-change-me";

    /// <summary>Minimum TokenSecret length outside Development/Test to raise the bar on brute-force attempts.</summary>
    public const int MinProductionTokenSecretLength = 32;

    /// <summary>Max distinct lines per cart (defensive bound, spec edge case 1).</summary>
    public int MaxLinesPerCart { get; set; } = 100;

    /// <summary>Token secret used for HMAC signing. Required in production.</summary>
    public string TokenSecret { get; set; } = DefaultTokenSecret;

    /// <summary>Token lifetime in days (FR-020).</summary>
    public int TokenLifetimeDays { get; set; } = 30;

    /// <summary>Idle window before cart is considered "abandoned" (FR-010).</summary>
    public int AbandonmentIdleMinutes { get; set; } = 60;

    /// <summary>Minimum gap between abandonment emissions per cart (FR-016).</summary>
    public int AbandonmentDedupeHours { get; set; } = 24;

    /// <summary>Guest cart purge threshold (FR-021).</summary>
    public int GuestCartPurgeDays { get; set; } = 30;

    /// <summary>Archived cart purge threshold (spec data-model.md state machine).</summary>
    public int ArchivedCartRetentionDays { get; set; } = 7;

    public int AbandonmentWorkerIntervalSeconds { get; set; } = 60;
    public int GuestCleanupWorkerIntervalSeconds { get; set; } = 3600;
    public int ArchivedReaperWorkerIntervalSeconds { get; set; } = 3600;
}

/// <summary>
/// Start-up validator for CartOptions. Non-positive window/threshold values would let the
/// abandonment worker re-emit every tick or mis-flag freshly touched carts as idle; reject
/// those at DI-composition time so operators see the misconfiguration at boot rather than
/// as silent bad behavior in production.
/// </summary>
public sealed class CartOptionsValidator(IHostEnvironment hostEnvironment) : IValidateOptions<CartOptions>
{
    public ValidateOptionsResult Validate(string? name, CartOptions options)
    {
        var failures = new List<string>();
        if (options.AbandonmentIdleMinutes <= 0) failures.Add("Cart:AbandonmentIdleMinutes must be positive.");
        if (options.AbandonmentDedupeHours <= 0) failures.Add("Cart:AbandonmentDedupeHours must be positive.");
        if (options.GuestCartPurgeDays <= 0) failures.Add("Cart:GuestCartPurgeDays must be positive.");
        if (options.ArchivedCartRetentionDays <= 0) failures.Add("Cart:ArchivedCartRetentionDays must be positive.");
        if (options.MaxLinesPerCart <= 0) failures.Add("Cart:MaxLinesPerCart must be positive.");
        if (options.TokenLifetimeDays <= 0) failures.Add("Cart:TokenLifetimeDays must be positive.");
        if (string.IsNullOrWhiteSpace(options.TokenSecret)) failures.Add("Cart:TokenSecret must be set.");

        // Production/Staging MUST NOT ship with the well-known default secret or a short one —
        // a leaked default would let any caller forge a valid cart_token and hijack any
        // anonymous cart by hash collision via brute force.
        var isDev = hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test");
        if (!isDev && !string.IsNullOrWhiteSpace(options.TokenSecret))
        {
            if (string.Equals(options.TokenSecret, CartOptions.DefaultTokenSecret, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Cart:TokenSecret is the well-known default in environment '{hostEnvironment.EnvironmentName}'. "
                    + "Override it via configuration / Key Vault before shipping.");
            }
            if (options.TokenSecret.Length < CartOptions.MinProductionTokenSecretLength)
            {
                failures.Add(
                    $"Cart:TokenSecret must be at least {CartOptions.MinProductionTokenSecretLength} chars "
                    + $"in environment '{hostEnvironment.EnvironmentName}'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
