namespace BackendApi.Modules.Cart.Primitives;

public sealed class CartOptions
{
    public const string SectionName = "Cart";

    /// <summary>Max distinct lines per cart (defensive bound, spec edge case 1).</summary>
    public int MaxLinesPerCart { get; set; } = 100;

    /// <summary>Token secret used for HMAC signing. Required in production.</summary>
    public string TokenSecret { get; set; } = "dev-only-cart-token-secret-change-me";

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
