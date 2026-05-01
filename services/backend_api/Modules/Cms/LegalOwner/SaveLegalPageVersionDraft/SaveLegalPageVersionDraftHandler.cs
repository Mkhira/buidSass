using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.LegalOwner.SaveLegalPageVersionDraft;

/// <summary>
/// Create/update a legal-page version draft per spec 024 contract §5.
/// Pipeline: load (or create) → reject if non-draft → xmin guard on update
/// → apply → save → audit (cms.draft.created / cms.draft.updated).
/// </summary>
public sealed class SaveLegalPageVersionDraftHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SaveLegalPageVersionDraftHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveLegalPageVersionDraftResult> HandleAsync(
        Guid? id,
        string legalPageKindWire,
        Guid actorId,
        string actorRole,
        SaveLegalPageVersionDraftRequest body,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        LegalPageVersion row;
        bool isCreate = id is null;

        if (isCreate)
        {
            row = new LegalPageVersion
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                LegalPageKindWire = legalPageKindWire,
                StateWire = ContentLifecycleState.Draft.ToWire(),
                OwnerActorId = actorId,
            };
        }
        else
        {
            var loaded = await _db.LegalPageVersions
                .FirstOrDefaultAsync(v => v.Id == id, ct);
            if (loaded is null)
            {
                return SaveLegalPageVersionDraftResult.Reject(
                    CmsReasonCode.PreviewEntityNotFound, "Legal page version not found.", 404);
            }
            if (loaded.LegalPageKindWire != legalPageKindWire)
            {
                return SaveLegalPageVersionDraftResult.Reject(
                    CmsReasonCode.LegalPageNotFoundForMarket,
                    "Legal page version kind mismatch.", 404);
            }
            if (loaded.StateWire != ContentLifecycleState.Draft.ToWire())
            {
                return SaveLegalPageVersionDraftResult.Reject(
                    CmsReasonCode.DraftNotEditable,
                    "Only draft legal page versions are editable.", 400);
            }
            if (body.Xmin is null || loaded.Xmin != body.Xmin.Value)
            {
                return SaveLegalPageVersionDraftResult.Reject(
                    CmsReasonCode.LegalPageVersionConflict,
                    "Legal page version row was modified since it was loaded.", 409);
            }
            row = loaded;
        }

        ApplyToRow(row, body, nowUtc);

        if (isCreate)
        {
            await _db.LegalPageVersions.AddAsync(row, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isCreate ? "cms.draft.created" : "cms.draft.updated",
            EntityType: "cms.legal_page_version",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new
            {
                row.Id,
                row.LegalPageKindWire,
                row.MarketCode,
                row.StateWire,
                row.VersionLabel,
                row.EffectiveAtUtc,
                row.Xmin,
            },
            Reason: null), ct);

        return isCreate
            ? SaveLegalPageVersionDraftResult.Created(row)
            : SaveLegalPageVersionDraftResult.Updated(row);
    }

    private static void ApplyToRow(
        LegalPageVersion row,
        SaveLegalPageVersionDraftRequest body,
        DateTimeOffset nowUtc)
    {
        row.VersionLabel = body.VersionLabel;
        row.BodyAr = body.BodyAr;
        row.BodyEn = body.BodyEn;
        row.EffectiveAtUtc = body.EffectiveAtUtc;
        row.MarketCode = body.MarketCode;
        row.EditorSaveAtUtc = nowUtc;
    }
}
