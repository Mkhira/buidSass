using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.SaveFaqEntryDraft;

public sealed record SaveFaqEntryDraftResult(
    bool IsSuccess,
    FaqEntry? Row,
    string? ReasonCode,
    string? Detail,
    int Status)
{
    public static SaveFaqEntryDraftResult Created(FaqEntry row) => new(true, row, null, null, 201);
    public static SaveFaqEntryDraftResult Updated(FaqEntry row) => new(true, row, null, null, 200);
    public static SaveFaqEntryDraftResult Reject(string reason, string detail, int status) =>
        new(false, null, reason, detail, status);
}

public sealed class SaveFaqEntryDraftHandler
{
    private static readonly HashSet<string> SupportedMarkets = new(StringComparer.Ordinal)
        { "EG", "KSA", "*" };

    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SaveFaqEntryDraftHandler(CmsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<SaveFaqEntryDraftResult> HandleAsync(
        Guid? id,
        Guid actorId,
        string actorRole,
        SaveFaqEntryDraftRequest body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return SaveFaqEntryDraftResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing, "Request body is required.", 400);
        }
        if (string.IsNullOrWhiteSpace(body.Category))
        {
            return SaveFaqEntryDraftResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing, "category is required.", 400);
        }
        if (!SupportedMarkets.Contains(body.MarketCode))
        {
            return SaveFaqEntryDraftResult.Reject(
                CmsReasonCode.StorefrontMarketUnsupported,
                $"Unsupported market_code '{body.MarketCode}'.", 400);
        }
        if (body.QuestionAr is { Length: > 250 } || body.QuestionEn is { Length: > 250 })
        {
            return SaveFaqEntryDraftResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing,
                "question must be ≤ 250 chars.", 400);
        }
        if (body.AnswerAr is { Length: > 4000 } || body.AnswerEn is { Length: > 4000 })
        {
            return SaveFaqEntryDraftResult.Reject(
                CmsReasonCode.BlogBodyTooLong,
                "answer must be ≤ 4000 chars.", 400);
        }

        var nowUtc = _clock.GetUtcNow();
        FaqEntry row;
        var isCreate = id is null;

        if (isCreate)
        {
            row = new FaqEntry
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = nowUtc,
                StateWire = ContentLifecycleState.Draft.ToWire(),
                OwnerActorId = actorId,
            };
        }
        else
        {
            var loaded = await _db.FaqEntries.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (loaded is null)
            {
                return SaveFaqEntryDraftResult.Reject(
                    CmsReasonCode.PreviewEntityNotFound, "FAQ entry not found.", 404);
            }
            if (loaded.StateWire != ContentLifecycleState.Draft.ToWire())
            {
                return SaveFaqEntryDraftResult.Reject(
                    CmsReasonCode.DraftNotEditable, "Only draft FAQ entries are editable.", 400);
            }
            if (body.Xmin is null || loaded.Xmin != body.Xmin.Value)
            {
                return SaveFaqEntryDraftResult.Reject(
                    CmsReasonCode.DraftVersionConflict,
                    "FAQ entry was modified since it was loaded.", 409);
            }
            row = loaded;
        }

        row.CategoryWire = body.Category;
        row.QuestionAr = body.QuestionAr;
        row.QuestionEn = body.QuestionEn;
        row.AnswerAr = body.AnswerAr;
        row.AnswerEn = body.AnswerEn;
        if (body.DisplayOrder is int o) row.DisplayOrder = o;
        row.MarketCode = body.MarketCode;
        row.ScheduledPublishAtUtc = body.ScheduledPublishAtUtc;
        row.EditorSaveAtUtc = nowUtc;

        if (isCreate) await _db.FaqEntries.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: isCreate ? "cms.draft.created" : "cms.draft.updated",
            EntityType: "cms.faq_entry",
            EntityId: row.Id,
            BeforeState: null,
            AfterState: new { row.Id, row.CategoryWire, row.MarketCode, row.StateWire, row.Xmin },
            Reason: null), ct);

        return isCreate ? SaveFaqEntryDraftResult.Created(row) : SaveFaqEntryDraftResult.Updated(row);
    }
}
