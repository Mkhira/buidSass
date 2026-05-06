namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-memory test double for <see cref="IOrderLinkedReadContract"/>. Tests
/// pre-stage rows by id; production binding (spec 011) replaces this in DI.
/// </summary>
public sealed class FakeOrderLinkedReadContract : IOrderLinkedReadContract
{
    private readonly Dictionary<(string kind, Guid id), LinkedEntityReadResult> _store = new();

    public FakeOrderLinkedReadContract Stage(string kind, LinkedEntityReadResult result)
    {
        _store[(kind, result.LinkedEntityId)] = result;
        return this;
    }

    public Task<LinkedEntityReadResult?> ReadAsync(string kind, Guid linkedEntityId, Guid actorCustomerId, CancellationToken ct)
    {
        if (_store.TryGetValue((kind, linkedEntityId), out var hit))
        {
            return Task.FromResult<LinkedEntityReadResult?>(hit);
        }
        return Task.FromResult<LinkedEntityReadResult?>(null);
    }
}
