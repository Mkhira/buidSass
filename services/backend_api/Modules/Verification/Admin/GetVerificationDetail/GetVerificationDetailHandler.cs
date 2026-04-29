using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Admin.GetVerificationDetail;

/// <summary>
/// Spec 020 contracts §3.2. Loads the verification + its snapshotted schema +
/// every transition + every document's metadata (bodies fetched separately via
/// OpenHistoricalDocument). Calls <see cref="IRegulatorAssistLookup"/> — V1's
/// <c>NullRegulatorAssistLookup</c> always returns null so the field is absent.
/// </summary>
public sealed class GetVerificationDetailHandler(
    VerificationDbContext db,
    IRegulatorAssistLookup regulatorAssist,
    IPiiAccessRecorder piiRecorder)
{
    public async Task<DetailResult> HandleAsync(
        Guid verificationId,
        IReadOnlySet<string> reviewerMarkets,
        CancellationToken ct)
    {
        var verification = await db.Verifications
            .AsNoTracking()
            .Where(v => v.Id == verificationId)
            .Select(v => new
            {
                v.Id,
                v.CustomerId,
                v.MarketCode,
                v.SchemaVersion,
                v.Profession,
                v.RegulatorIdentifier,
                v.State,
                v.SubmittedAt,
                v.DecidedAt,
                v.DecidedBy,
                v.ExpiresAt,
                v.SupersedesId,
                v.SupersededById,
                v.VoidReason,
                v.Xmin,
            })
            .SingleOrDefaultAsync(ct);

        if (verification is null)
        {
            return DetailResult.NotFound;
        }

        // Market scope: 404 (not 403) if the row is in another market — avoids leaking existence.
        if (!reviewerMarkets.Contains(verification.MarketCode))
        {
            return DetailResult.NotFound;
        }

        var schema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == verification.MarketCode && s.Version == verification.SchemaVersion)
            .SingleOrDefaultAsync(ct);

        var transitions = await db.StateTransitions
            .AsNoTracking()
            .Where(t => t.VerificationId == verificationId)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new TransitionPayload(
                t.Id, t.PriorState, t.NewState, t.ActorKind, t.ActorId, t.Reason, t.MetadataJson, t.OccurredAt))
            .ToListAsync(ct);

        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => d.VerificationId == verificationId)
            .OrderBy(d => d.UploadedAt)
            .Select(d => new DocumentMetadataPayload(
                d.Id, d.ContentType, d.SizeBytes, d.ScanStatus, d.UploadedAt, d.PurgedAt))
            .ToListAsync(ct);

        // FR-015a-e: every read of a regulator identifier (LicenseNumber) or
        // document metadata MUST flow through IPiiAccessRecorder so the
        // verification.pii_access audit row is written. The reviewer detail
        // payload exposes both, so we record one event per kind. The recorder
        // is idempotent on (verification, kind, request-correlation).
        await piiRecorder.RecordAsync(PiiAccessKind.LicenseNumberRead, verificationId, documentId: null, ct);
        if (documents.Count > 0)
        {
            await piiRecorder.RecordAsync(PiiAccessKind.DocumentMetadataRead, verificationId, documentId: null, ct);
        }

        // Regulator-assist (FR-016b). V1 default is null; never blocks.
        object? regulatorAssistResult = null;
        try
        {
            var lookup = await regulatorAssist.LookupAsync(
                verification.MarketCode,
                verification.RegulatorIdentifier,
                ct);
            if (lookup is not null)
            {
                regulatorAssistResult = new
                {
                    register_found = lookup.RegisterFound,
                    status = lookup.Status,
                    issued_date = lookup.IssuedDate?.ToString("yyyy-MM-dd"),
                    expiry_date = lookup.ExpiryDate?.ToString("yyyy-MM-dd"),
                    full_name_in_register = lookup.FullNameInRegister,
                };
            }
        }
        catch
        {
            // FR-016a — assistive lookup MUST NEVER block a state transition or
            // a detail render. Surface as null on failure.
            regulatorAssistResult = null;
        }

        // Customer locale — phase 3 batch 2 will resolve this from spec 004's
        // identity context. For now default to "ar" (KSA + EG default-locale
        // per spec 003); the response shape is forward-compatible.
        const string customerLocale = "ar";

        var schemaPayload = schema is null
            ? new SchemaSnapshotPayload(
                MarketCode: verification.MarketCode,
                Version: verification.SchemaVersion,
                RequiredFieldsJson: "[]",
                AllowedDocumentTypesJson: "[]",
                RetentionMonths: 0,
                CooldownDays: 0,
                ExpiryDays: 0,
                ReminderWindowsDaysJson: "[]",
                SlaDecisionBusinessDays: 0,
                SlaWarningBusinessDays: 0)
            : new SchemaSnapshotPayload(
                MarketCode: schema.MarketCode,
                Version: schema.Version,
                RequiredFieldsJson: schema.RequiredFieldsJson,
                AllowedDocumentTypesJson: schema.AllowedDocumentTypesJson,
                RetentionMonths: schema.RetentionMonths,
                CooldownDays: schema.CooldownDays,
                ExpiryDays: schema.ExpiryDays,
                ReminderWindowsDaysJson: schema.ReminderWindowsDaysJson,
                SlaDecisionBusinessDays: schema.SlaDecisionBusinessDays,
                SlaWarningBusinessDays: schema.SlaWarningBusinessDays);

        var response = new GetVerificationDetailResponse(
            Id: verification.Id,
            CustomerId: verification.CustomerId,
            MarketCode: verification.MarketCode,
            SchemaVersion: verification.SchemaVersion,
            Profession: verification.Profession,
            RegulatorIdentifier: verification.RegulatorIdentifier,
            State: verification.State.ToWireValue(),
            SubmittedAt: verification.SubmittedAt,
            DecidedAt: verification.DecidedAt,
            DecidedBy: verification.DecidedBy,
            ExpiresAt: verification.ExpiresAt,
            SupersedesId: verification.SupersedesId,
            SupersededById: verification.SupersededById,
            VoidReason: verification.VoidReason,
            CustomerLocale: customerLocale,
            SchemaSnapshot: schemaPayload,
            Transitions: transitions,
            Documents: documents,
            RegulatorAssist: regulatorAssistResult,
            Xmin: verification.Xmin);

        return DetailResult.Found(response);
    }
}

public sealed record DetailResult(bool Exists, GetVerificationDetailResponse? Response)
{
    public static DetailResult Found(GetVerificationDetailResponse r) => new(true, r);
    public static DetailResult NotFound => new(false, null);
}
