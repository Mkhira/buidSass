namespace BackendApi.Modules.Shared.Testing;

public sealed class FakeReviewLinkedReadContract : IReviewLinkedReadContract
{
    private readonly Dictionary<Guid, LinkedEntityReadResult> _store = new();

    public FakeReviewLinkedReadContract Stage(LinkedEntityReadResult result)
    {
        _store[result.LinkedEntityId] = result;
        return this;
    }

    public Task<LinkedEntityReadResult?> ReadAsync(Guid reviewId, Guid actorCustomerId, CancellationToken ct)
        => Task.FromResult(_store.TryGetValue(reviewId, out var hit) ? hit : null);
}
