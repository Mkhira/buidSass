namespace BackendApi.Modules.Shared;

/// <summary>
/// Read return-request rows for ticket-link ownership + market resolution.
/// Spec 013 implements; spec 023 consumes.
/// </summary>
public interface IReturnLinkedReadContract
{
    Task<LinkedEntityReadResult?> ReadAsync(
        Guid returnRequestId,
        Guid actorCustomerId,
        CancellationToken ct);
}
