using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Shipping.Providers;
using FluentAssertions;

namespace Shipping.Tests.Unit;

public sealed class ProviderWebhookSignatureTests
{
    // AC-10 — invalid signature returns 401 (validation returns false here).
    [Fact]
    public void Empty_body_fails_validation()
    {
        ProviderWebhookSignature.ValidateHexHmacSha256(
            ReadOnlySpan<byte>.Empty, "abcdef", "secret").Should().BeFalse();
    }

    [Fact]
    public void Empty_secret_fails_validation()
    {
        var body = "{\"a\":1}"u8.ToArray();
        var sig = ComputeHex(body, "secret");
        ProviderWebhookSignature.ValidateHexHmacSha256(body, sig, "").Should().BeFalse();
    }

    [Fact]
    public void Correct_signature_passes()
    {
        var body = "{\"awb_number\":\"X\",\"event_kind\":\"in_transit\"}"u8.ToArray();
        var secret = "test-secret";
        var sig = ComputeHex(body, secret);
        ProviderWebhookSignature.ValidateHexHmacSha256(body, sig, secret).Should().BeTrue();
    }

    [Fact]
    public void Sha256_prefix_accepted()
    {
        var body = "{}"u8.ToArray();
        var secret = "k";
        var sig = "sha256=" + ComputeHex(body, secret);
        ProviderWebhookSignature.ValidateHexHmacSha256(body, sig, secret).Should().BeTrue();
    }

    [Fact]
    public void Tampered_body_fails()
    {
        var original = "{\"a\":1}"u8.ToArray();
        var tampered = "{\"a\":2}"u8.ToArray();
        var sig = ComputeHex(original, "s");
        ProviderWebhookSignature.ValidateHexHmacSha256(tampered, sig, "s").Should().BeFalse();
    }

    [Fact]
    public void Malformed_hex_fails()
    {
        ProviderWebhookSignature.ValidateHexHmacSha256(
            "{}"u8.ToArray(), "zz", "s").Should().BeFalse();
    }

    private static string ComputeHex(byte[] body, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
}
