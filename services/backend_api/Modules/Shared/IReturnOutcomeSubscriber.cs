namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 013 publishes; spec 023 subscribes to auto-resolve the originating
/// ticket when its converted return-request reaches a terminal outcome.
/// All implementations MUST be idempotent — events may be redelivered.
/// </summary>
public interface IReturnOutcomeSubscriber
{
    Task OnReturnCompletedAsync(ReturnCompletedNotice notice, CancellationToken ct);
    Task OnReturnRejectedAsync(ReturnRejectedNotice notice, CancellationToken ct);
}

public sealed record ReturnCompletedNotice(
    Guid ReturnRequestId,
    Guid? OriginatingTicketId,
    DateTimeOffset CompletedAtUtc);

public sealed record ReturnRejectedNotice(
    Guid ReturnRequestId,
    Guid? OriginatingTicketId,
    string ReasonNote,
    DateTimeOffset RejectedAtUtc);
