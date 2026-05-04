using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Customer.GetMyQuote;

/// <summary>
/// Spec 021 contract §2.4 — full quote detail response. Includes:
/// <list type="bullet">
///   <item>Core quote fields (id, state, market, company / branch, PO, requested/expires/decided timestamps).</item>
///   <item><c>current_version</c> — the latest published <see cref="GetMyQuoteVersionDetail"/>
///         with line items + totals (raw jsonb passthrough — UI parses).</item>
///   <item><c>prior_versions</c> — metadata only for every prior version (no line items
///         re-rendered; PDFs available via §2.8 download).</item>
///   <item><c>next_action</c> — derived enum vocabulary that tells the UI which CTA
///         to surface. See <see cref="NextActionVocabulary"/>.</item>
/// </list>
/// </summary>
public sealed record GetMyQuoteResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("decided_at")] DateTimeOffset? DecidedAt,
    [property: JsonPropertyName("terminal_at")] DateTimeOffset? TerminalAt,
    [property: JsonPropertyName("invoice_billing")] bool InvoiceBilling,
    [property: JsonPropertyName("current_version")] GetMyQuoteVersionDetail? CurrentVersion,
    [property: JsonPropertyName("prior_versions")] IReadOnlyList<GetMyQuoteVersionMetadata> PriorVersions,
    [property: JsonPropertyName("next_action")] string? NextAction);

/// <summary>
/// Latest published version — full body. Line-items / totals / terms shipped as
/// raw jsonb-string passthrough so the UI can parse without the API needing to
/// commit to a schema-versioned binding (the jsonb shape evolves with US3).
/// </summary>
public sealed record GetMyQuoteVersionDetail(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version_number")] int VersionNumber,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("line_items")] string LineItemsJson,
    [property: JsonPropertyName("terms_text")] string TermsTextJson,
    [property: JsonPropertyName("terms_days")] int TermsDays,
    [property: JsonPropertyName("validity_extends")] bool ValidityExtends,
    [property: JsonPropertyName("totals_summary")] string TotalsSummaryJson,
    [property: JsonPropertyName("customer_revision_comment")] string? CustomerRevisionCommentJson);

/// <summary>
/// Prior-version metadata — id, version_number, published_at — no line items re-rendered.
/// UI uses this to populate a version-history picker that links to the §2.8 PDF download.
/// </summary>
public sealed record GetMyQuoteVersionMetadata(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version_number")] int VersionNumber,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

/// <summary>
/// Spec §2.4 <c>next_action</c> derived vocabulary. The handler computes this from
/// quote state + expires_at; UI switches on the token to pick the CTA. Stable
/// strings — adding a value is a contract-breaking change.
/// </summary>
public static class NextActionVocabulary
{
    /// <summary>State has no actionable next step (terminal states + Drafted = admin-only-visible).</summary>
    public const string None = null!;

    /// <summary>Quote is <c>revised</c> — buyer can request another revision.</summary>
    public const string RequestRevision = "request_revision";

    /// <summary>Quote is <c>revised</c> AND ≥ 1 published version — buyer can submit acceptance.</summary>
    public const string SubmitAcceptance = "submit_acceptance";

    /// <summary>Quote is <c>expired</c> AND was previously <c>revised</c> — buyer can ask for a renewal (Cycle C-3 / spec gap, V1.5).</summary>
    public const string RenewNow = "renew_now";
}
