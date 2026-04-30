namespace BackendApi.Modules.Reviews.Customer.GetReportReasons;

/// <summary>
/// GET /api/customer/reviews/report-reasons per contract §2.6 — static lookup of
/// the 5 fixed reasons + their ICU keys. Always returns the same payload; safe to
/// cache aggressively client-side.
/// </summary>
public sealed class GetReportReasonsHandler
{
    private static readonly IReadOnlyList<ReportReasonItem> Reasons = new[]
    {
        new ReportReasonItem(
            Reason: "inappropriate_language",
            I18nKeys: new ReportReasonI18n(
                En: "review.report.reason.inappropriate_language.en",
                Ar: "review.report.reason.inappropriate_language.ar"),
            RequiresNote: false),
        new ReportReasonItem(
            Reason: "spam_or_irrelevant",
            I18nKeys: new ReportReasonI18n(
                En: "review.report.reason.spam_or_irrelevant.en",
                Ar: "review.report.reason.spam_or_irrelevant.ar"),
            RequiresNote: false),
        new ReportReasonItem(
            Reason: "personal_attack",
            I18nKeys: new ReportReasonI18n(
                En: "review.report.reason.personal_attack.en",
                Ar: "review.report.reason.personal_attack.ar"),
            RequiresNote: false),
        new ReportReasonItem(
            Reason: "false_or_misleading",
            I18nKeys: new ReportReasonI18n(
                En: "review.report.reason.false_or_misleading.en",
                Ar: "review.report.reason.false_or_misleading.ar"),
            RequiresNote: false),
        new ReportReasonItem(
            Reason: "other_with_required_note",
            I18nKeys: new ReportReasonI18n(
                En: "review.report.reason.other_with_required_note.en",
                Ar: "review.report.reason.other_with_required_note.ar"),
            RequiresNote: true),
    };

    public GetReportReasonsResponse Handle() => new(Reasons);
}

public sealed record ReportReasonItem(string Reason, ReportReasonI18n I18nKeys, bool RequiresNote);
public sealed record ReportReasonI18n(string En, string Ar);
public sealed record GetReportReasonsResponse(IReadOnlyList<ReportReasonItem> Items);
