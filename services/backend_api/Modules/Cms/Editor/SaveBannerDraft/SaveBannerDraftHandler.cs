using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.SaveBannerDraft;

/// <summary>
/// Handles POST /v1/admin/cms/banner-slots/drafts (create) and
/// PATCH /v1/admin/cms/banner-slots/drafts/{id} (update) per spec 024
/// contract §3.1 / §3.2 + quickstart §1 code skeleton.
///
/// Pipeline: validate request → resolve row (create or load) → reject if
/// non-draft → xmin guard on update → apply changes → save → audit
/// (cms.draft.created / cms.draft.updated).
/// </summary>
public sealed class SaveBannerDraftHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SaveBannerDraftHandler(
        CmsDbContext db,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveBannerDraftResult> HandleAsync(
        Guid? id,
        Guid actorId,
        string actorRole,
        SaveBannerDraftRequest body,
        CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow();
        BannerSlot row;
        bool isCreate = id is null;

        if (isCreate)
        {
            row = new BannerSlot
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                StateWire = ContentLifecycleState.Draft.ToWire(),
                OwnerActorId = actorId,
            };
        }
        else
        {
            var loaded = await _db.BannerSlots.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (loaded is null)
            {
                return SaveBannerDraftResult.Reject(
                    CmsReasonCode.PreviewEntityNotFound, "Banner not found.", 404);
            }

            if (loaded.StateWire != ContentLifecycleState.Draft.ToWire())
            {
                return SaveBannerDraftResult.Reject(
                    CmsReasonCode.DraftNotEditable,
                    "Only draft banners are editable.",
                    400);
            }

            if (body.Xmin is null || loaded.Xmin != body.Xmin.Value)
            {
                return SaveBannerDraftResult.Reject(
                    CmsReasonCode.DraftVersionConflict,
                    "Banner row was modified since it was loaded.",
                    409);
            }

            row = loaded;
        }

        ApplyToRow(row, body, nowUtc);

        if (isCreate)
        {
            await _db.BannerSlots.AddAsync(row, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isCreate ? "cms.draft.created" : "cms.draft.updated",
            EntityType: "cms.banner_slot",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new
            {
                row.Id,
                row.SlotKindWire,
                row.MarketCode,
                row.StateWire,
                row.Xmin,
            },
            Reason: null), ct);

        return isCreate ? SaveBannerDraftResult.Created(row) : SaveBannerDraftResult.Updated(row);
    }

    private static void ApplyToRow(BannerSlot row, SaveBannerDraftRequest body, DateTimeOffset nowUtc)
    {
        row.SlotKindWire = body.SlotKind;
        row.HeadlineAr = body.HeadlineAr;
        row.HeadlineEn = body.HeadlineEn;
        row.SubheadAr = body.SubheadAr;
        row.SubheadEn = body.SubheadEn;
        row.AssetIdAr = body.AssetIdAr;
        row.AssetIdEn = body.AssetIdEn;
        row.CtaKindWire = body.CtaKind;
        row.CtaTarget = body.CtaTarget;
        row.ScheduledStartUtc = body.ScheduledStartUtc;
        row.ScheduledEndUtc = body.ScheduledEndUtc;
        row.MarketCode = body.MarketCode;
        if (body.PriorityWithinSlot is int p) row.PriorityWithinSlot = p;
        row.EditorSaveAtUtc = nowUtc;
    }
}
