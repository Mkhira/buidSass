namespace BackendApi.Modules.Shared;

/// <summary>
/// Read quote rows for ticket-link ownership + B2B authorization + market resolution.
/// Spec 021 implements; spec 023 consumes.
/// </summary>
public interface IQuoteLinkedReadContract
{
    Task<LinkedEntityReadResult?> ReadAsync(
        Guid quoteId,
        Guid actorCustomerId,
        CancellationToken ct);
}
