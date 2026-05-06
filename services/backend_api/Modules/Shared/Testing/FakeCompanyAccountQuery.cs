namespace BackendApi.Modules.Shared.Testing;

public sealed class FakeCompanyAccountQuery : ICompanyAccountQuery
{
    private readonly Dictionary<Guid, Guid?> _map = new();

    public FakeCompanyAccountQuery Stage(Guid customerId, Guid? companyId)
    {
        _map[customerId] = companyId;
        return this;
    }

    public Task<Guid?> ResolveCompanyIdAsync(Guid customerId, CancellationToken ct)
        => Task.FromResult(_map.TryGetValue(customerId, out var hit) ? hit : null);
}
