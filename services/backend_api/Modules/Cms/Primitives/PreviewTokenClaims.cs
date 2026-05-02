namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Value object carrying the HMAC-signed claims for a CMS preview token.
/// Per spec 024 research §R3 + data-model §9. The token itself is opaque on
/// the wire; this record materialises its claim set on the server during
/// minting (write) and verification (read).
/// </summary>
public sealed record PreviewTokenClaims(
    EntityKind EntityKind,
    Guid EntityId,
    Guid VersionId,
    DateTimeOffset MintTimestampUtc,
    int TtlSeconds,
    string ActorRoleAtMint)
{
    public DateTimeOffset ExpiresAtUtc => MintTimestampUtc.AddSeconds(TtlSeconds);

    public bool IsExpiredAt(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;
}
