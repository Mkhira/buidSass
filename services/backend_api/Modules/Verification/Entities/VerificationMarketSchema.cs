namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// Versioned per-market verification schema. A change is INSERT new version +
/// UPDATE prior version's <see cref="EffectiveTo"/> = now() in one Tx; rows are
/// never mutated otherwise. See spec 020 data-model §2.4.
/// </summary>
public sealed class VerificationMarketSchema
{
    /// <summary>"eg" or "ksa". Composite PK part 1.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Monotonic per market. Composite PK part 2.</summary>
    public int Version { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>NULL = currently active. Unique partial index enforces ≤ 1 active per market.</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>jsonb — list of <c>RequiredFieldSpec</c> entries.</summary>
    public string RequiredFieldsJson { get; set; } = "[]";

    /// <summary>jsonb — array of MIME strings.</summary>
    public string AllowedDocumentTypesJson { get; set; } = "[\"application/pdf\",\"image/jpeg\",\"image/png\",\"image/heic\"]";

    public int RetentionMonths { get; set; }
    public int CooldownDays { get; set; }
    public int ExpiryDays { get; set; }

    /// <summary>jsonb — descending list of integers (default <c>[30, 14, 7, 1]</c>).</summary>
    public string ReminderWindowsDaysJson { get; set; } = "[30, 14, 7, 1]";

    public int SlaDecisionBusinessDays { get; set; } = 2;
    public int SlaWarningBusinessDays { get; set; } = 1;

    /// <summary>jsonb — array of ISO date strings (YYYY-MM-DD).</summary>
    public string HolidaysListJson { get; set; } = "[]";
}
