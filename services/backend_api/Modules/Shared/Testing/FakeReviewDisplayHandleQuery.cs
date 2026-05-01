namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process fake of <see cref="IReviewDisplayHandleQuery"/>. Looks up a
/// supplied dictionary; unknown customer ids return <see langword="null"/>
/// (callers fall back to the FR-016a render rule).
/// </summary>
public sealed class FakeReviewDisplayHandleQuery : IReviewDisplayHandleQuery
{
    private readonly IReadOnlyDictionary<Guid, CustomerDisplayInfo> _byId;

    public FakeReviewDisplayHandleQuery(IReadOnlyDictionary<Guid, CustomerDisplayInfo> byId)
    {
        _byId = byId;
    }

    public static FakeReviewDisplayHandleQuery Empty { get; } = new(new Dictionary<Guid, CustomerDisplayInfo>());

    public Task<CustomerDisplayInfo?> GetAsync(Guid customerId, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(customerId, out var info) ? info : null);
}
