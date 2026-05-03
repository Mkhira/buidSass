namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 repeat-order template stub (data-model §2.10). V1 ships persistence + the
/// "save as template" endpoint only; full management UI is Phase 1.5-c (FR-038).
///
/// Uniqueness enforced via two partial indexes (research §R12):
/// <list type="bullet">
///   <item><c>(company_id, name) WHERE company_id IS NOT NULL</c> — company-owned.</item>
///   <item><c>(user_id, name) WHERE company_id IS NULL</c> — individual-owned.</item>
/// </list>
/// </summary>
public sealed class RepeatOrderTemplate
{
    public Guid Id { get; set; }

    /// <summary>Two-letter market code (mirrors the source quote's market); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>FK to the <see cref="Quote"/> the template was saved from.</summary>
    public Guid SourceQuoteId { get; set; }

    /// <summary>Company-owned templates carry the company id; individual-owned templates leave this NULL.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Logical FK to spec 004's user identity.</summary>
    public Guid UserId { get; set; }

    /// <summary>Bilingual name jsonb <c>{ en?, ar? }</c> — at least one locale required.</summary>
    public string NameJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }
}
