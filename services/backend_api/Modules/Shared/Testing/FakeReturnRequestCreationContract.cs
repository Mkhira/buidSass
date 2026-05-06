namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// Test double for spec 013's <see cref="IReturnRequestCreationContract"/>.
/// Captures every invocation; idempotent on identical <c>idempotencyKey</c>.
/// </summary>
public sealed class FakeReturnRequestCreationContract : IReturnRequestCreationContract
{
    private readonly Dictionary<string, Guid> _byIdempotencyKey = new();
    public List<(Guid customerId, Guid orderLineId, string narrative, Guid originatingTicketId, string idempotencyKey)> Invocations { get; } = new();

    public Task<ReturnRequestCreationResult> CreateAsync(
        Guid customerId,
        Guid orderLineId,
        string narrative,
        IReadOnlyList<Guid> attachmentIds,
        Guid originatingTicketId,
        string idempotencyKey,
        CancellationToken ct)
    {
        Invocations.Add((customerId, orderLineId, narrative, originatingTicketId, idempotencyKey));

        if (_byIdempotencyKey.TryGetValue(idempotencyKey, out var existing))
        {
            return Task.FromResult(new ReturnRequestCreationResult(true, existing, null, true));
        }

        var newId = Guid.NewGuid();
        _byIdempotencyKey[idempotencyKey] = newId;
        return Task.FromResult(new ReturnRequestCreationResult(true, newId, null, false));
    }
}
