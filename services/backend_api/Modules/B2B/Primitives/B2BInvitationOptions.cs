namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Bound from configuration section <c>B2B:Invitation</c>. Holds the HMAC-SHA256 signing
/// key used by <see cref="CompanyInvitationTokenHasher"/> to derive
/// <c>company_invitations.token_hash</c> from the plaintext token. Rotating the key
/// invalidates outstanding pending invitations by design.
/// </summary>
public sealed class B2BInvitationOptions
{
    public const string SectionName = "B2B:Invitation";

    /// <summary>
    /// Base64-encoded HMAC key (≥ 32 bytes). MUST be supplied by configuration in
    /// non-Test environments (production/staging via Key Vault, dev via user-secrets).
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;
}
