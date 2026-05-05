using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Admin.GetQuoteDetail;

public sealed record GetQuoteDetailResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("customer_id")] Guid CustomerId,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("decided_at")] DateTimeOffset? DecidedAt,
    [property: JsonPropertyName("terminal_at")] DateTimeOffset? TerminalAt,
    [property: JsonPropertyName("current_version_id")] Guid? CurrentVersionId,
    [property: JsonPropertyName("invoice_billing")] bool InvoiceBilling,
    [property: JsonPropertyName("customer_locale")] string CustomerLocale,
    [property: JsonPropertyName("restriction_policy_snapshot")] string RestrictionPolicySnapshot,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("draft_body")] string? DraftBody,
    [property: JsonPropertyName("versions")] IReadOnlyList<GetQuoteDetailVersion> Versions,
    [property: JsonPropertyName("transitions")] IReadOnlyList<GetQuoteDetailTransition> Transitions,
    [property: JsonPropertyName("verification_warnings")] IReadOnlyList<GetQuoteDetailWarning> VerificationWarnings,
    [property: JsonPropertyName("archived_sku_lines")] IReadOnlyList<string> ArchivedSkuLines);

public sealed record GetQuoteDetailVersion(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version_number")] int VersionNumber,
    [property: JsonPropertyName("authored_by")] Guid AuthoredBy,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("line_items")] string LineItems,
    [property: JsonPropertyName("terms_text")] string TermsText,
    [property: JsonPropertyName("terms_days")] int TermsDays,
    [property: JsonPropertyName("validity_extends")] bool ValidityExtends,
    [property: JsonPropertyName("totals_summary")] string TotalsSummary,
    [property: JsonPropertyName("customer_revision_comment")] string? CustomerRevisionComment);

public sealed record GetQuoteDetailTransition(
    [property: JsonPropertyName("prior_state")] string PriorState,
    [property: JsonPropertyName("new_state")] string NewState,
    [property: JsonPropertyName("actor_kind")] string ActorKind,
    [property: JsonPropertyName("actor_id")] Guid? ActorId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record GetQuoteDetailWarning(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("message_key")] string MessageKey);
