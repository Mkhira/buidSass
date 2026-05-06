using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Null-fallback implementations of the cross-module read contracts. Registered
/// via <c>TryAddScoped</c> so production specs (011 / 013 / 020 / 021 / 022) win
/// at registration when their modules are loaded; otherwise these stubs keep
/// the module bootable for development + integration tests that don't need
/// real cross-module data.
/// </summary>
public sealed class NullOrderLinkedReadContract : IOrderLinkedReadContract
{
    public Task<LinkedEntityReadResult?> ReadAsync(string kind, Guid linkedEntityId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult<LinkedEntityReadResult?>(null);
}

public sealed class NullReturnLinkedReadContract : IReturnLinkedReadContract
{
    public Task<LinkedEntityReadResult?> ReadAsync(Guid returnRequestId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult<LinkedEntityReadResult?>(null);
}

public sealed class NullQuoteLinkedReadContract : IQuoteLinkedReadContract
{
    public Task<LinkedEntityReadResult?> ReadAsync(Guid quoteId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult<LinkedEntityReadResult?>(null);
}

public sealed class NullReviewLinkedReadContract : IReviewLinkedReadContract
{
    public Task<LinkedEntityReadResult?> ReadAsync(Guid reviewId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult<LinkedEntityReadResult?>(null);
}

public sealed class NullVerificationLinkedReadContract : IVerificationLinkedReadContract
{
    public Task<LinkedEntityReadResult?> ReadAsync(Guid verificationId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult<LinkedEntityReadResult?>(null);
}

public sealed class NullCompanyAccountQuery : ICompanyAccountQuery
{
    public Task<Guid?> ResolveCompanyIdAsync(Guid customerId, CancellationToken ct)
        => Task.FromResult<Guid?>(null);
}

public sealed class NullReturnRequestCreationContract : IReturnRequestCreationContract
{
    public Task<ReturnRequestCreationResult> CreateAsync(
        Guid customerId,
        Guid orderLineId,
        string narrative,
        IReadOnlyList<Guid> attachmentIds,
        Guid originatingTicketId,
        string idempotencyKey,
        CancellationToken ct)
        => Task.FromResult(new ReturnRequestCreationResult(
            Success: false,
            ReturnRequestId: null,
            ReasonCode: TicketReasonCode.ReturnCreationContractFailed,
            Idempotent: false));
}
