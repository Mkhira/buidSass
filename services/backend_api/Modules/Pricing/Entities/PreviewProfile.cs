namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Spec 007-b admin-only saved sample customer + cart context for the preview tool.
/// Data-model §2.6. Visibility is <c>personal</c> by default; promotion to <c>shared</c>
/// requires <c>commercial.approver</c> and is audited.
/// </summary>
public sealed class PreviewProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MarketCode { get; set; } = string.Empty;
    public string Locale { get; set; } = "en"; // ar | en
    public string AccountKind { get; set; } = "consumer"; // consumer | b2b
    public Guid? TierId { get; set; }
    public string VerificationState { get; set; } = "none"; // none | submitted | approved | rejected | expired
    /// <summary>
    /// JSONB column. Schema: <c>[{ "sku": string, "qty": int, "restricted": bool }]</c>;
    /// max 50 entries (data-model §2.6).
    /// </summary>
    public string CartLinesJson { get; set; } = "[]";
    public string Visibility { get; set; } = "personal"; // personal | shared
    public Guid CreatedBy { get; set; }
    public Guid? VendorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint XminRowVersion { get; set; }
}
