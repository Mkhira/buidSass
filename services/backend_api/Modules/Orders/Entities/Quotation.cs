namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Quotation sibling aggregate (research R3). B2B quote-first flow: draft → active → accepted
/// /rejected/expired/converted. Spec 011 ships the data model + internal handlers; the admin
/// CRUD endpoints land in a follow-up PR (see tasks.md Phase E10).
/// </summary>
public sealed class Quotation
{
    public const string StatusDraft = "draft";
    public const string StatusActive = "active";
    public const string StatusAccepted = "accepted";
    public const string StatusRejected = "rejected";
    public const string StatusExpired = "expired";
    public const string StatusConverted = "converted";

    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Status { get; set; } = StatusDraft;
    public Guid PriceExplanationId { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<QuotationLine> Lines { get; set; } = new();
}
