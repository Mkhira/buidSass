namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Per-market policy snapshot loaded from <c>support.support_market_schemas</c>.
/// Pure value object; mutable only through <c>UpdateMarketSchemaCommand</c>.
/// </summary>
public sealed record SupportMarketPolicy(
    string MarketCode,
    bool AutoAssignmentEnabled,
    int ReopenWindowDays,
    int MaxReopenCount,
    int AutoCloseAfterResolvedDays,
    int AttachmentMaxPerTicket,
    int AttachmentMaxSizeMb,
    int AttachmentCumulativeMaxMb,
    string[] AllowedMimeTypes)
{
    public bool ReopenEnabled => ReopenWindowDays > 0;
    public bool AutoCloseEnabled => AutoCloseAfterResolvedDays > 0;
    public long AttachmentMaxSizeBytes => AttachmentMaxSizeMb * 1024L * 1024L;
    public long AttachmentCumulativeMaxBytes => AttachmentCumulativeMaxMb * 1024L * 1024L;

    public static readonly string[] DefaultAllowedMimeTypes =
    {
        "image/png", "image/jpeg", "image/webp", "application/pdf", "video/mp4",
    };

    public static SupportMarketPolicy DefaultFor(string marketCode) => new(
        MarketCode: marketCode,
        AutoAssignmentEnabled: false,
        ReopenWindowDays: 14,
        MaxReopenCount: 3,
        AutoCloseAfterResolvedDays: 7,
        AttachmentMaxPerTicket: 10,
        AttachmentMaxSizeMb: 10,
        AttachmentCumulativeMaxMb: 50,
        AllowedMimeTypes: DefaultAllowedMimeTypes);
}
