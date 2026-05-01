using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;

/// <summary>
/// PATCH /api/admin/reviews/policy/markets/{market_code} per contract §4.4.
/// Partial update — any subset of policy knobs may be supplied; absent fields
/// are left unchanged. Per-field range validation mirrors the database
/// CHECK constraints from <c>ReviewsMarketSchemaConfiguration</c> so callers
/// fail-fast at the app layer with a stable reason code.
/// </summary>
public sealed class UpdateMarketSchemaHandler
{
    private readonly ReviewsDbContext _db;
    private readonly TimeProvider _time;

    public UpdateMarketSchemaHandler(ReviewsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<UpdateMarketSchemaResult> HandleAsync(
        Guid actorId,
        string marketCode,
        UpdateMarketSchemaRequest body,
        CancellationToken ct)
    {
        if (body.EligibilityWindowDays is { } e
            && (e < 30 || e > 730))
        {
            return Reject("eligibility_window_days must be 30-730.");
        }
        if (body.EditWindowDays is { } ed && (ed < 7 || ed > 90))
        {
            return Reject("edit_window_days must be 7-90.");
        }
        if (body.CommunityReportThreshold is { } th && (th < 1 || th > 10))
        {
            return Reject("community_report_threshold must be 1-10.");
        }
        if (body.CommunityReportWindowDays is { } cw && cw <= 0)
        {
            return Reject("community_report_window_days must be > 0.");
        }
        if (body.ReportQualifyingAccountAgeDays is { } ag && (ag < 0 || ag > 90))
        {
            return Reject("report_qualifying_account_age_days must be 0-90.");
        }
        if (body.PendingModerationSlaHours is { } sla && sla <= 0)
        {
            return Reject("pending_moderation_sla_hours must be > 0.");
        }

        var schema = await _db.MarketSchemas.FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (schema is null)
        {
            return UpdateMarketSchemaResult.Reject(404, ReviewReasonCode.AggregateMarketInvalid,
                $"Market schema for '{marketCode}' not found.");
        }

        if (body.EligibilityWindowDays is { } e2) schema.EligibilityWindowDays = e2;
        if (body.EditWindowDays is { } ed2) schema.EditWindowDays = ed2;
        if (body.CommunityReportThreshold is { } th2) schema.CommunityReportThreshold = th2;
        if (body.CommunityReportWindowDays is { } cw2) schema.CommunityReportWindowDays = cw2;
        if (body.ReportQualifyingAccountAgeDays is { } ag2) schema.ReportQualifyingAccountAgeDays = ag2;
        if (body.ReportQualifyingRequiresVerifiedBuyer is { } vb)
            schema.ReportQualifyingRequiresVerifiedBuyer = vb;
        if (body.PendingModerationSlaHours is { } sla2) schema.PendingModerationSlaHours = sla2;

        schema.UpdatedAtUtc = _time.GetUtcNow();
        schema.UpdatedByActorId = actorId;

        await _db.SaveChangesAsync(ct);

        return UpdateMarketSchemaResult.Success(new UpdateMarketSchemaResponse(
            schema.MarketCode,
            schema.EligibilityWindowDays,
            schema.EditWindowDays,
            schema.CommunityReportThreshold,
            schema.CommunityReportWindowDays,
            schema.ReportQualifyingAccountAgeDays,
            schema.ReportQualifyingRequiresVerifiedBuyer,
            schema.PendingModerationSlaHours,
            schema.UpdatedAtUtc));
    }

    private static UpdateMarketSchemaResult Reject(string detail) =>
        UpdateMarketSchemaResult.Reject(400, ReviewReasonCode.PolicyMarketValueOutOfRange, detail);
}

public sealed record UpdateMarketSchemaRequest(
    int? EligibilityWindowDays,
    int? EditWindowDays,
    int? CommunityReportThreshold,
    int? CommunityReportWindowDays,
    int? ReportQualifyingAccountAgeDays,
    bool? ReportQualifyingRequiresVerifiedBuyer,
    int? PendingModerationSlaHours);

public sealed record UpdateMarketSchemaResponse(
    string MarketCode,
    int EligibilityWindowDays,
    int EditWindowDays,
    int CommunityReportThreshold,
    int CommunityReportWindowDays,
    int ReportQualifyingAccountAgeDays,
    bool ReportQualifyingRequiresVerifiedBuyer,
    int PendingModerationSlaHours,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdateMarketSchemaResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    UpdateMarketSchemaResponse? Response)
{
    public static UpdateMarketSchemaResult Success(UpdateMarketSchemaResponse r) => new(true, 200, null, null, r);
    public static UpdateMarketSchemaResult Reject(int s, string c, string d) => new(false, s, c, d, null);
}
