namespace BackendApi.Modules.Shared;

/// <summary>
/// Read verification-submission rows for ticket <c>verification_query</c>
/// category linkage. Spec 020 implements; spec 023 consumes.
/// </summary>
public interface IVerificationLinkedReadContract
{
    Task<LinkedEntityReadResult?> ReadAsync(
        Guid verificationId,
        Guid actorCustomerId,
        CancellationToken ct);
}
