namespace BackendApi.Modules.Shared.Testing;

public sealed class FakeVerificationLinkedReadContract : IVerificationLinkedReadContract
{
    private readonly Dictionary<Guid, LinkedEntityReadResult> _store = new();

    public FakeVerificationLinkedReadContract Stage(LinkedEntityReadResult result)
    {
        _store[result.LinkedEntityId] = result;
        return this;
    }

    public Task<LinkedEntityReadResult?> ReadAsync(Guid verificationId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult(_store.TryGetValue(verificationId, out var hit) ? hit : null);
}
