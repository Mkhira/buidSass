using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.ListAdminNotes;

public sealed class ListAdminNotesHandler
{
    private readonly ReviewsDbContext _db;

    public ListAdminNotesHandler(ReviewsDbContext db) => _db = db;

    public async Task<ListAdminNotesResponse> HandleAsync(Guid reviewId, CancellationToken ct)
    {
        var notes = await _db.AdminNotes.AsNoTracking()
            .Where(n => n.ReviewId == reviewId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new AdminNoteListItem(n.Id, n.ActorId, n.Note, n.CreatedAtUtc))
            .ToListAsync(ct);
        return new ListAdminNotesResponse(notes);
    }
}

public sealed record AdminNoteListItem(Guid Id, Guid ActorId, string Note, DateTimeOffset CreatedAtUtc);
public sealed record ListAdminNotesResponse(IReadOnlyList<AdminNoteListItem> Items);
