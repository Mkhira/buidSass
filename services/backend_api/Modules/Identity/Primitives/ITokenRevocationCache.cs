namespace BackendApi.Modules.Identity.Primitives;

public interface ITokenRevocationCache
{
    bool MightContain(ReadOnlySpan<byte> tokenHash);
    ValueTask RefreshAsync(CancellationToken cancellationToken);
}

public interface IRefreshTokenRevocationStore
{
    Task RevokeAsync(byte[] tokenHash, string reason, Guid? actorId, CancellationToken cancellationToken);
    Task RevokeBySessionAsync(Guid sessionId, string reason, Guid? actorId, CancellationToken cancellationToken);
    Task<bool> IsRevokedAsync(byte[] tokenHash, CancellationToken cancellationToken);
}
