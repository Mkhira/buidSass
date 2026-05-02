using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.SaveFeaturedSectionDraft;

public sealed record SaveFeaturedSectionDraftResult(
    bool IsSuccess,
    FeaturedSection? Row,
    string? ReasonCode,
    string? Detail,
    int Status,
    IDictionary<string, object?>? Extensions = null)
{
    public static SaveFeaturedSectionDraftResult Created(FeaturedSection row) => new(true, row, null, null, 201);
    public static SaveFeaturedSectionDraftResult Updated(FeaturedSection row) => new(true, row, null, null, 200);
    public static SaveFeaturedSectionDraftResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}

public sealed class SaveFeaturedSectionDraftHandler
{
    private static readonly HashSet<string> SupportedReferenceKinds = new(StringComparer.Ordinal)
        { "product", "category", "bundle" };
    private static readonly HashSet<string> SupportedSectionKinds = new(StringComparer.Ordinal)
        { "home_top", "home_mid", "category_landing", "b2b_landing" };
    private static readonly HashSet<string> SupportedMarkets = new(StringComparer.Ordinal)
        { "EG", "KSA", "*" };

    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SaveFeaturedSectionDraftHandler(CmsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveFeaturedSectionDraftResult> HandleAsync(
        Guid? id,
        Guid actorId,
        string actorRole,
        SaveFeaturedSectionDraftRequest body,
        int featuredSectionMaxReferences,
        CancellationToken ct)
    {
        if (body is null)
        {
            return SaveFeaturedSectionDraftResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing, "Request body is required.", 400);
        }
        if (!SupportedSectionKinds.Contains(body.SectionKind))
        {
            return SaveFeaturedSectionDraftResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing,
                $"Unsupported section_kind '{body.SectionKind}'.", 400);
        }
        if (!SupportedMarkets.Contains(body.MarketCode))
        {
            return SaveFeaturedSectionDraftResult.Reject(
                CmsReasonCode.StorefrontMarketUnsupported,
                $"Unsupported market_code '{body.MarketCode}'.", 400);
        }
        if (body.References is null || body.References.Count == 0)
        {
            return SaveFeaturedSectionDraftResult.Reject(
                CmsReasonCode.FeaturedSectionEmptyReferences,
                "references[] must contain at least one reference.", 400);
        }
        if (body.References.Count > featuredSectionMaxReferences)
        {
            return SaveFeaturedSectionDraftResult.Reject(
                CmsReasonCode.FeaturedSectionTooManyReferences,
                $"references[] exceeds the per-market cap of {featuredSectionMaxReferences}.", 400);
        }
        foreach (var r in body.References)
        {
            if (!SupportedReferenceKinds.Contains(r.Kind))
            {
                return SaveFeaturedSectionDraftResult.Reject(
                    CmsReasonCode.FeaturedSectionReferenceKindUnsupported,
                    $"Unsupported reference kind '{r.Kind}'.", 400);
            }
        }

        var nowUtc = _clock.GetUtcNow();
        FeaturedSection row;
        var isCreate = id is null;

        if (isCreate)
        {
            row = new FeaturedSection
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                StateWire = ContentLifecycleState.Draft.ToWire(),
                OwnerActorId = actorId,
            };
        }
        else
        {
            var loaded = await _db.FeaturedSections.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (loaded is null)
            {
                return SaveFeaturedSectionDraftResult.Reject(
                    CmsReasonCode.PreviewEntityNotFound, "Featured section not found.", 404);
            }
            if (loaded.StateWire != ContentLifecycleState.Draft.ToWire())
            {
                return SaveFeaturedSectionDraftResult.Reject(
                    CmsReasonCode.DraftNotEditable, "Only draft featured sections are editable.", 400);
            }
            if (body.Xmin is null || loaded.Xmin != body.Xmin.Value)
            {
                return SaveFeaturedSectionDraftResult.Reject(
                    CmsReasonCode.DraftVersionConflict,
                    "Featured section row was modified since it was loaded.", 409);
            }
            row = loaded;
        }

        row.SectionKindWire = body.SectionKind;
        row.TitleAr = body.TitleAr;
        row.TitleEn = body.TitleEn;
        row.SubtitleAr = body.SubtitleAr;
        row.SubtitleEn = body.SubtitleEn;
        row.ReferencesJson = JsonSerializer.Serialize(
            body.References.Select(r => new { kind = r.Kind, id = r.Id }));
        if (body.DisplayPriority is int p) row.DisplayPriority = p;
        row.MarketCode = body.MarketCode;
        row.ScheduledPublishAtUtc = body.ScheduledPublishAtUtc;
        row.EditorSaveAtUtc = nowUtc;

        if (isCreate) await _db.FeaturedSections.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isCreate ? "cms.draft.created" : "cms.draft.updated",
            EntityType: "cms.featured_section",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new { row.Id, row.SectionKindWire, row.MarketCode, row.StateWire, row.Xmin },
            Reason: null), ct);

        return isCreate
            ? SaveFeaturedSectionDraftResult.Created(row)
            : SaveFeaturedSectionDraftResult.Updated(row);
    }
}
