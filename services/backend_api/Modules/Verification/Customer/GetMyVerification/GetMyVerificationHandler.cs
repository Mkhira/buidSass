using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Customer.GetMyVerification;

/// <summary>
/// Spec 020 contracts §2.4 / tasks T057. Single verification by id, owner-
/// gated. Returns the row + transition history (customer-relevant fields
/// only — no internal metadata jsonb) + document metadata (no bodies).
/// Foreign-id (different customer) returns NotFound (not Forbidden) to
/// avoid leaking row existence.
/// </summary>
public sealed class GetMyVerificationHandler(VerificationDbContext db)
{
    public async Task<GetMyVerificationResponse?> HandleAsync(
        Guid customerId,
        Guid verificationId,
        CancellationToken ct)
    {
        var verification = await db.Verifications
            .AsNoTracking()
            .Where(v => v.Id == verificationId && v.CustomerId == customerId)
            .SingleOrDefaultAsync(ct);

        if (verification is null)
        {
            return null;
        }

        var transitions = await db.StateTransitions
            .AsNoTracking()
            .Where(t => t.VerificationId == verificationId)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new TransitionPayload(
                t.PriorState, t.NewState, t.OccurredAt, t.Reason))
            .ToListAsync(ct);

        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => d.VerificationId == verificationId)
            .OrderBy(d => d.UploadedAt)
            .Select(d => new DocumentMetadataPayload(
                d.Id,
                d.ContentType,
                d.SizeBytes,
                d.ScanStatus,
                d.UploadedAt,
                d.PurgedAt != null))
            .ToListAsync(ct);

        return new GetMyVerificationResponse(
            Id: verification.Id,
            State: verification.State.ToWireValue(),
            MarketCode: verification.MarketCode,
            Profession: verification.Profession,
            SubmittedAt: verification.SubmittedAt,
            DecidedAt: verification.DecidedAt,
            ExpiresAt: verification.ExpiresAt,
            SupersedesId: verification.SupersedesId,
            SupersededById: verification.SupersededById,
            Transitions: transitions,
            Documents: documents);
    }
}
