using System.Collections.Generic;

namespace BackendApi.Modules.Verification.Primitives;

/// <summary>
/// Read-only value-object resolved from a <c>verification_market_schemas</c> row
/// (data-model §2.4). Every market-aware decision handler reads from this object
/// rather than hardcoding constants — Principle 5 (Market Configuration).
/// </summary>
/// <param name="MarketCode">"eg" or "ksa".</param>
/// <param name="Version">Monotonic schema version per market.</param>
/// <param name="EffectiveFrom">When this version became active.</param>
/// <param name="EffectiveTo">When superseded; null if currently active.</param>
/// <param name="RequiredFields">jsonb-shaped form schema (each entry: name, type, optional pattern, etc).</param>
/// <param name="AllowedDocumentTypes">MIME types accepted on <c>verification_documents</c>.</param>
/// <param name="RetentionMonths">Months that documents survive a terminal-state transition before purge.</param>
/// <param name="CooldownDays">Days a customer must wait after rejection before re-submitting.</param>
/// <param name="ExpiryDays">Days from approval until <c>expires_at</c>.</param>
/// <param name="ReminderWindowsDays">Descending list of expiry-reminder offsets (default <c>[30, 14, 7, 1]</c>).</param>
/// <param name="SlaDecisionBusinessDays">Reviewer must decide within this many business days from <c>submitted_at</c> (FR-031 / SC-007).</param>
/// <param name="SlaWarningBusinessDays">Surface a warning at this threshold (FR-031).</param>
/// <param name="HolidaysList">UTC dates excluded from business-day arithmetic.</param>
public sealed record VerificationMarketPolicy(
    string MarketCode,
    int Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<RequiredFieldSpec> RequiredFields,
    IReadOnlySet<string> AllowedDocumentTypes,
    int RetentionMonths,
    int CooldownDays,
    int ExpiryDays,
    IReadOnlyList<int> ReminderWindowsDays,
    int SlaDecisionBusinessDays,
    int SlaWarningBusinessDays,
    IReadOnlyList<DateOnly> HolidaysList)
{
    /// <summary>
    /// Returns the SLA breach UTC instant for a verification submitted at
    /// <paramref name="submittedAt"/>. Reviewer is in breach when
    /// <c>now() &gt; this</c> for non-terminal verifications.
    /// </summary>
    public DateTimeOffset SlaBreachAt(DateTimeOffset submittedAt) =>
        BusinessDayCalculator.AddBusinessDays(
            submittedAt,
            SlaDecisionBusinessDays,
            BusinessDayCalculator.DefaultWeekend,
            HolidaysList);

    /// <summary>
    /// Returns the SLA warning UTC instant.
    /// </summary>
    public DateTimeOffset SlaWarningAt(DateTimeOffset submittedAt) =>
        BusinessDayCalculator.AddBusinessDays(
            submittedAt,
            SlaWarningBusinessDays,
            BusinessDayCalculator.DefaultWeekend,
            HolidaysList);

    /// <summary>
    /// Earliest UTC instant at which a customer may re-submit after rejection.
    /// </summary>
    public DateTimeOffset CooldownEndsAt(DateTimeOffset rejectedAt) =>
        rejectedAt.AddDays(CooldownDays);

    /// <summary>
    /// UTC instant the next-issued approval will expire.
    /// </summary>
    public DateTimeOffset ApprovalExpiresAt(DateTimeOffset approvedAt) =>
        approvedAt.AddDays(ExpiryDays);

    /// <summary>
    /// UTC instant a document may be purged once its parent verification
    /// transitioned terminal.
    /// </summary>
    public DateTimeOffset DocumentPurgeAt(DateTimeOffset terminalAt) =>
        terminalAt.AddMonths(RetentionMonths);
}

/// <summary>
/// Single entry in the <c>required_fields</c> jsonb on a market schema.
/// Validation ships in Phase 2 / 3 handlers; the shape is locked here so the
/// reviewer queue can render the form snapshot exactly as the customer saw it
/// (FR-026).
/// </summary>
/// <param name="Name">Field key.</param>
/// <param name="Kind">"text", "enum", "date", "tel", etc.</param>
/// <param name="Required">Hard-required — submission rejects without it.</param>
/// <param name="Pattern">Optional regex (used when <see cref="Kind"/> = "text" or "tel").</param>
/// <param name="EnumValues">Optional choices (used when <see cref="Kind"/> = "enum").</param>
/// <param name="LabelKeyEn">ICU key for the EN label.</param>
/// <param name="LabelKeyAr">ICU key for the AR label.</param>
public sealed record RequiredFieldSpec(
    string Name,
    string Kind,
    bool Required,
    string? Pattern,
    IReadOnlyList<string>? EnumValues,
    string LabelKeyEn,
    string LabelKeyAr);
