using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.AddAdminNote;

public sealed class AddAdminNoteHandler
{
    private readonly ReviewsDbContext _db;
    private readonly TimeProvider _time;

    public AddAdminNoteHandler(ReviewsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AddAdminNoteResult> HandleAsync(
        Guid actorId, Guid reviewId, string note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length < 10 || note.Length > 4000)
        {
            return AddAdminNoteResult.Reject(400, ReviewReasonCode.ModerationReasonRequired,
                "Note must be 10–4000 characters.");
        }

        var exists = await _db.Reviews.AnyAsync(r => r.Id == reviewId, ct);
        if (!exists)
        {
            return AddAdminNoteResult.Reject(404, ReviewReasonCode.ModerationInvalidState, "Review not found.");
        }

        var nowUtc = _time.GetUtcNow();
        var entity = new ReviewAdminNote
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ActorId = actorId,
            Note = note,
            CreatedAtUtc = nowUtc,
        };
        _db.AdminNotes.Add(entity);
        await _db.SaveChangesAsync(ct);

        return AddAdminNoteResult.Success(new AddAdminNoteResponse(entity.Id, entity.CreatedAtUtc));
    }
}

public sealed record AddAdminNoteRequest(string Note);
public sealed record AddAdminNoteResponse(Guid NoteId, DateTimeOffset CreatedAtUtc);

public sealed record AddAdminNoteResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    AddAdminNoteResponse? Response)
{
    public static AddAdminNoteResult Success(AddAdminNoteResponse r) => new(true, 201, null, null, r);
    public static AddAdminNoteResult Reject(int s, string c, string d) => new(false, s, c, d, null);
}
