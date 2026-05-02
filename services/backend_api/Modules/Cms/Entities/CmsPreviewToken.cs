namespace BackendApi.Modules.Cms.Entities;

/// <summary>Preview token store row per spec 024 data-model §2.7.</summary>
public sealed class CmsPreviewToken
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the opaque token. The token itself is NEVER persisted.</summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    public string EntityKindWire { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid VersionId { get; set; }
    public string ActorRoleAtMint { get; set; } = string.Empty;
    public Guid MintedByActorId { get; set; }
    public DateTimeOffset MintedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? RevokedByActorId { get; set; }
}
