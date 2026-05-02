using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cms.Editor.BulkReorderFaqEntries;

public sealed record FaqEntryReorderItem(Guid Id, int DisplayOrder, uint Xmin);

public sealed record BulkReorderFaqEntriesRequest(IReadOnlyList<FaqEntryReorderItem> Entries);

public sealed record BulkReorderFaqEntriesResult(
    bool IsSuccess,
    int UpdatedCount,
    string? ReasonCode,
    string? Detail,
    int Status,
    IReadOnlyList<Guid>? ConflictRows = null)
{
    public static BulkReorderFaqEntriesResult Ok(int updated) => new(true, updated, null, null, 200);
    public static BulkReorderFaqEntriesResult Reject(
        string reason, string detail, int status, IReadOnlyList<Guid>? conflicts = null) =>
        new(false, 0, reason, detail, status, conflicts);
}

/// <summary>
/// POST /v1/admin/cms/faq-entries/reorder per spec 024 contract §3.7.
/// Atomic reorder: any row failing the xmin precondition rolls back the whole
/// batch and returns 409 cms.faq.reorder_conflict listing the conflicted ids.
/// </summary>
public sealed class BulkReorderFaqEntriesHandler
{
    private readonly CmsDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public BulkReorderFaqEntriesHandler(CmsDbContext db, IAuditEventPublisher audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<BulkReorderFaqEntriesResult> HandleAsync(
        BulkReorderFaqEntriesRequest body,
        Guid actorId,
        string actorRole,
        CancellationToken ct)
    {
        if (body.Entries is null || body.Entries.Count == 0)
        {
            return BulkReorderFaqEntriesResult.Reject(
                CmsReasonCode.PublishLocaleCompletenessMissing,
                "entries[] must contain at least one item.", 400);
        }

        var ids = body.Entries.Select(e => e.Id).ToList();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var rows = await _db.FaqEntries
            .Where(f => ids.Contains(f.Id))
            .ToListAsync(ct);

        var conflicts = new List<Guid>();
        foreach (var item in body.Entries)
        {
            var row = rows.FirstOrDefault(r => r.Id == item.Id);
            if (row is null)
            {
                conflicts.Add(item.Id);
                continue;
            }
            if (row.Xmin != item.Xmin)
            {
                conflicts.Add(item.Id);
            }
        }

        if (conflicts.Count > 0)
        {
            await tx.RollbackAsync(ct);
            return BulkReorderFaqEntriesResult.Reject(
                CmsReasonCode.FaqReorderConflict,
                $"{conflicts.Count} row(s) modified concurrently.",
                409, conflicts);
        }

        var nowUtc = _clock.GetUtcNow();
        foreach (var item in body.Entries)
        {
            var row = rows.First(r => r.Id == item.Id);
            row.DisplayOrder = item.DisplayOrder;
            row.EditorSaveAtUtc = nowUtc;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return BulkReorderFaqEntriesResult.Reject(
                CmsReasonCode.FaqReorderConflict,
                "Concurrent reorder detected at commit.",
                409, ids);
        }

        await _audit.PublishAsync(new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: "cms.faq.reordered",
            EntityType: "cms.faq_entry",
            EntityId: Guid.Empty,
            BeforeState: null,
            AfterState: new { Count = body.Entries.Count, Ids = ids },
            Reason: null), ct);

        return BulkReorderFaqEntriesResult.Ok(body.Entries.Count);
    }
}
