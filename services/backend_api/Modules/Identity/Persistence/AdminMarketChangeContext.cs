namespace BackendApi.Modules.Identity.Persistence;

public interface IAdminMarketChangeContext
{
    bool IsActive { get; }
    IDisposable BeginScope();
}

public sealed class AdminMarketChangeContext : IAdminMarketChangeContext
{
    private static readonly AsyncLocal<int> ScopeDepth = new();

    public bool IsActive => ScopeDepth.Value > 0;

    public IDisposable BeginScope()
    {
        ScopeDepth.Value = ScopeDepth.Value + 1;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ScopeDepth.Value = Math.Max(0, ScopeDepth.Value - 1);
            _disposed = true;
        }
    }
}
