namespace BackendApi.Modules.Shared;

/// <summary>
/// Idempotent return-request creation invoked from spec 023's
/// "convert ticket to return-request" flow (FR-028). Spec 013 implements.
/// Same <paramref name="idempotencyKey"/> on retry MUST return the existing
/// return-request id without creating a duplicate (per research §R-02).
/// </summary>
public interface IReturnRequestCreationContract
{
    Task<ReturnRequestCreationResult> CreateAsync(
        Guid customerId,
        Guid orderLineId,
        string narrative,
        IReadOnlyList<Guid> attachmentIds,
        Guid originatingTicketId,
        string idempotencyKey,
        CancellationToken ct);
}

public sealed record ReturnRequestCreationResult(
    bool Success,
    Guid? ReturnRequestId,
    string? ReasonCode,
    bool Idempotent);
