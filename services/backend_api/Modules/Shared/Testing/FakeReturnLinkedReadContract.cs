namespace BackendApi.Modules.Shared.Testing;

public sealed class FakeReturnLinkedReadContract : IReturnLinkedReadContract
{
    private readonly Dictionary<Guid, LinkedEntityReadResult> _store = new();

    public FakeReturnLinkedReadContract Stage(LinkedEntityReadResult result)
    {
        _store[result.LinkedEntityId] = result;
        return this;
    }

    public Task<LinkedEntityReadResult?> ReadAsync(Guid returnRequestId, Guid actorCustomerId, CancellationToken ct)
    {
        return Task.FromResult(_store.TryGetValue(returnRequestId, out var hit) ? hit : null);
    }
}
