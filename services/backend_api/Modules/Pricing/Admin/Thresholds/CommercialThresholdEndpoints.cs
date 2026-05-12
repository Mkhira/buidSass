using System.Text.Json;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.Thresholds;

/// <summary>
/// Spec 007-b commercial-threshold endpoints (US5 / contract §9). Per-market
/// high-impact gate tuning knobs (FR-025). GET is open to any commercial
/// operator (read-only); PATCH is super_admin-only and audited.
/// </summary>
public static class CommercialThresholdEndpoints
{
    public static IEndpointRouteBuilder MapCommercialThresholdEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/thresholds");
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };

        group.MapGet("/{marketCode}", GetAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission(CommercialPermissions.Operator);
        group.MapPatch("/{marketCode}", UpdateAsync)
            .RequireAuthorization(adminAuth)
            // Route accepts any commercial role; the handler enforces super_admin
            // for the actual mutation (so we can emit a precise reason code
            // instead of a generic 403).
            .RequirePermission(CommercialPermissions.Operator);
        return builder;
    }

    // -------------------- GET /thresholds/{marketCode} --------------------

    private static async Task<IResult> GetAsync(
        string marketCode,
        HttpContext context,
        PricingDbContext db,
        CancellationToken ct)
    {
        var key = NormaliseMarketCode(marketCode);
        if (key is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialThresholdInvalidValue,
                "Unknown market_code; expected SA or EG.");
        }
        var row = await db.CommercialThresholds.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MarketCode == key, ct);
        if (row is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.thresholds.unconfigured",
                "No threshold row configured for this market.");
        }
        return Results.Ok(ToResponse(row));
    }

    // -------------------- PATCH /thresholds/{marketCode} --------------------

    private static async Task<IResult> UpdateAsync(
        string marketCode,
        [FromBody] JsonElement bodyElement,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        CommercialActorPermissions perms,
        IPublisher events,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!await perms.HasSuperAdminAsync(context, ct))
        {
            return AdminCommercialResponseFactory.Problem(context, 403,
                CommercialReasonCode.CommercialThresholdForbidden,
                "Threshold mutation requires super_admin.");
        }

        var key = NormaliseMarketCode(marketCode);
        if (key is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialThresholdInvalidValue,
                "Unknown market_code; expected SA or EG.");
        }

        var (parsedOk, ifMatch) = AdminCommercialResponseFactory.TryParseIfMatch(context);
        if (!parsedOk)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Malformed If-Match header.");
        }

        var row = await db.CommercialThresholds.FirstOrDefaultAsync(t => t.MarketCode == key, ct);
        if (row is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 404,
                "commercial.thresholds.unconfigured",
                "No threshold row configured for this market.");
        }
        if (ifMatch is not null && ifMatch.Value != row.XminRowVersion)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Threshold was changed by another actor.");
        }

        var before = SnapshotForAudit(row);
        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var nowUtc = time.GetUtcNow();

        // PATCH semantics — explicit JSON property presence required to mutate.
        // null value disables the criterion; absent leaves it untouched.
        if (bodyElement.ValueKind != JsonValueKind.Object)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.CommercialThresholdInvalidValue,
                "Body must be a JSON object.");
        }

        if (TryReadBool(bodyElement, "gate_enabled", out var ge))
        {
            row.GateEnabled = ge;
        }
        if (TryReadDecimalProperty(bodyElement, "threshold_percent_off", out var pct))
        {
            if (pct is { } v && (v < 0 || v > 100))
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialThresholdInvalidValue,
                    "threshold_percent_off must be in [0, 100] or null.");
            }
            row.ThresholdPercentOff = pct;
        }
        if (TryReadLongProperty(bodyElement, "threshold_amount_off_minor", out var amt))
        {
            if (amt is { } a && a < 0)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialThresholdInvalidValue,
                    "threshold_amount_off_minor must be ≥ 0 or null.");
            }
            row.ThresholdAmountOffMinor = amt;
        }
        if (TryReadIntProperty(bodyElement, "threshold_duration_days", out var dur))
        {
            if (dur is { } d && d < 0)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialThresholdInvalidValue,
                    "threshold_duration_days must be ≥ 0 or null.");
            }
            row.ThresholdDurationDays = dur;
        }
        if (TryReadIntPropertyNonNull(bodyElement, "coupon_in_flight_grace_seconds", out var couponGrace))
        {
            if (couponGrace < 300 || couponGrace > 7200)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialThresholdInvalidValue,
                    "coupon_in_flight_grace_seconds must be within [300, 7200].");
            }
            row.CouponInFlightGraceSeconds = couponGrace;
        }
        if (TryReadIntPropertyNonNull(bodyElement, "promotion_in_flight_grace_seconds", out var promoGrace))
        {
            if (promoGrace < 300 || promoGrace > 7200)
            {
                return AdminCommercialResponseFactory.Problem(context, 400,
                    CommercialReasonCode.CommercialThresholdInvalidValue,
                    "promotion_in_flight_grace_seconds must be within [300, 7200].");
            }
            row.PromotionInFlightGraceSeconds = promoGrace;
        }

        row.UpdatedAtUtc = nowUtc;
        row.UpdatedByActorId = actorId;

        var after = SnapshotForAudit(row);
        var beforeJson = JsonSerializer.Serialize(before);
        var afterJson = JsonSerializer.Serialize(after);

        var publishAudit = audit.StageLocal(
            "commercial_threshold", Guid.Empty, "commercial.threshold_changed",
            actorId, "super_admin",
            before, after, diff: new { before, after },
            reasonNote: null, correlationId: null, nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.CommercialRowVersionConflict,
                "Threshold was changed by another actor.");
        }
        await publishAudit(ct);

        await events.Publish(new CommercialThresholdChanged(
            row.MarketCode, beforeJson, afterJson, actorId, nowUtc), ct);

        return Results.Ok(ToResponse(row));
    }

    // -------------------- helpers --------------------

    private static string? NormaliseMarketCode(string raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "SA" or "KSA" => "SA",
        "EG" => "EG",
        _ => null,
    };

    private static CommercialThresholdResponse ToResponse(CommercialThreshold t) =>
        new(
            MarketCode: t.MarketCode,
            GateEnabled: t.GateEnabled,
            ThresholdPercentOff: t.ThresholdPercentOff,
            ThresholdAmountOffMinor: t.ThresholdAmountOffMinor,
            ThresholdDurationDays: t.ThresholdDurationDays,
            CouponInFlightGraceSeconds: t.CouponInFlightGraceSeconds,
            PromotionInFlightGraceSeconds: t.PromotionInFlightGraceSeconds,
            UpdatedAtUtc: t.UpdatedAtUtc,
            UpdatedByActorId: t.UpdatedByActorId,
            RowVersion: t.XminRowVersion);

    private static object SnapshotForAudit(CommercialThreshold t) => new
    {
        t.MarketCode,
        t.GateEnabled,
        t.ThresholdPercentOff,
        t.ThresholdAmountOffMinor,
        t.ThresholdDurationDays,
        t.CouponInFlightGraceSeconds,
        t.PromotionInFlightGraceSeconds,
    };

    private static bool TryReadBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (!obj.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (prop.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }

    private static bool TryReadDecimalProperty(JsonElement obj, string name, out decimal? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) return true;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
        {
            value = d;
            return true;
        }
        return false;
    }

    private static bool TryReadLongProperty(JsonElement obj, string name, out long? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) return true;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var l))
        {
            value = l;
            return true;
        }
        return false;
    }

    private static bool TryReadIntProperty(JsonElement obj, string name, out int? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) return true;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i))
        {
            value = i;
            return true;
        }
        return false;
    }

    private static bool TryReadIntPropertyNonNull(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i))
        {
            value = i;
            return true;
        }
        return false;
    }
}
