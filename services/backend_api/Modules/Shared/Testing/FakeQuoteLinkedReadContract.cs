namespace BackendApi.Modules.Shared.Testing;

public sealed class FakeQuoteLinkedReadContract : IQuoteLinkedReadContract
{
    private readonly Dictionary<Guid, LinkedEntityReadResult> _store = new();

    public FakeQuoteLinkedReadContract Stage(LinkedEntityReadResult result)
    {
        _store[result.LinkedEntityId] = result;
        return this;
    }

    public Task<LinkedEntityReadResult?> ReadAsync(Guid quoteId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult(_store.TryGetValue(quoteId, out var hit) ? hit : null);
}
