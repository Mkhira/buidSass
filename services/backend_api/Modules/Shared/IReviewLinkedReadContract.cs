namespace BackendApi.Modules.Shared;

/// <summary>
/// Read review rows for ticket <c>review_dispute</c> category linkage.
/// Spec 022 implements; spec 023 consumes.
/// </summary>
public interface IReviewLinkedReadContract
{
    Task<LinkedEntityReadResult?> ReadAsync(
        Guid reviewId,
        Guid actorCustomerId,
        CancellationToken ct);
}
