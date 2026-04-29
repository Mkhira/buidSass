namespace BackendApi.Modules.Verification.Customer.GetMarketSchema;

/// <summary>
/// Active per-market verification schema returned to the customer-app form
/// renderer. Spec 020 task T100 / contracts §2.x. Locales render via the
/// <c>label_key_en</c> / <c>label_key_ar</c> ICU keys.
/// </summary>
/// <param name="MarketCode">Market this schema belongs to ("eg" or "ksa").</param>
/// <param name="Version">Monotonic version per market.</param>
/// <param name="EffectiveFrom">When this version became active.</param>
/// <param name="RequiredFieldsJson">jsonb — array of <c>RequiredFieldSpec</c> entries.</param>
/// <param name="AllowedDocumentTypesJson">jsonb — array of MIME strings.</param>
/// <param name="ExpiryDays">How long an approval lasts.</param>
/// <param name="CooldownDays">Customer cool-down after rejection.</param>
public sealed record GetMarketSchemaResponse(
    string MarketCode,
    int Version,
    DateTimeOffset EffectiveFrom,
    string RequiredFieldsJson,
    string AllowedDocumentTypesJson,
    int ExpiryDays,
    int CooldownDays);
