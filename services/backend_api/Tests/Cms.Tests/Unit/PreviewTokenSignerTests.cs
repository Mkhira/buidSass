using System.Text;
using BackendApi.Modules.Cms.Primitives;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class PreviewTokenSignerTests
{
    private static byte[] Key32() => Encoding.UTF8.GetBytes(new string('k', 32));

    private static PreviewTokenClaims SampleClaims() => new(
        EntityKind: EntityKind.BannerSlot,
        EntityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        VersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        MintTimestampUtc: DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
        TtlSeconds: 24 * 3600,
        ActorRoleAtMint: "cms.editor");

    [Fact]
    public void Sign_then_verify_round_trip_returns_claims()
    {
        var signer = new PreviewTokenSigner(Key32());
        var token = signer.Sign(SampleClaims());

        var verified = signer.Verify(token);

        verified.Should().BeEquivalentTo(SampleClaims());
    }

    [Fact]
    public void Tampered_token_throws_signature_invalid()
    {
        var signer = new PreviewTokenSigner(Key32());
        var token = signer.Sign(SampleClaims());

        // Flip one base64url char in the token (still valid charset).
        var tampered = token[..^4] + (token[^4] == 'A' ? 'B' : 'A') + token[^3..];
        var act = () => signer.Verify(tampered);

        act.Should().Throw<PreviewTokenSignatureInvalidException>();
    }

    [Fact]
    public void Different_key_throws_signature_invalid()
    {
        var signed = new PreviewTokenSigner(Key32()).Sign(SampleClaims());
        var verifier = new PreviewTokenSigner(Encoding.UTF8.GetBytes(new string('z', 32)));

        var act = () => verifier.Verify(signed);

        act.Should().Throw<PreviewTokenSignatureInvalidException>();
    }

    [Fact]
    public void Constructor_rejects_short_key()
    {
        var act = () => new PreviewTokenSigner(new byte[10]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hash_token_is_stable()
    {
        var token = "opaque-token-abc";
        var a = PreviewTokenSigner.HashToken(token);
        var b = PreviewTokenSigner.HashToken(token);

        a.Should().BeEquivalentTo(b);
        a.Length.Should().Be(32);
    }

    [Fact]
    public void Empty_token_throws_signature_invalid_on_verify()
    {
        var signer = new PreviewTokenSigner(Key32());
        var act = () => signer.Verify(string.Empty);
        act.Should().Throw<PreviewTokenSignatureInvalidException>();
    }

    [Fact]
    public void Expiry_helper_correct()
    {
        var c = SampleClaims();
        c.IsExpiredAt(c.MintTimestampUtc).Should().BeFalse();
        c.IsExpiredAt(c.MintTimestampUtc.AddSeconds(c.TtlSeconds - 1)).Should().BeFalse();
        c.IsExpiredAt(c.MintTimestampUtc.AddSeconds(c.TtlSeconds)).Should().BeTrue();
    }
}
