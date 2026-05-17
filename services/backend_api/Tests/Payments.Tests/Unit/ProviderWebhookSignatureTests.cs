using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.Payments.Providers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>T045 + AC-24 — V-3 fail-closed signature validation.</summary>
public sealed class ProviderWebhookSignatureTests
{
    private static HttpRequest BuildRequest(string headerName, string headerValue)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[headerName] = headerValue;
        return ctx.Request;
    }

    [Fact]
    public void Hex_ValidSignature_Passes()
    {
        var secret = "shhh";
        var body = Encoding.UTF8.GetBytes("{\"id\":\"abc\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(body));
        var req = BuildRequest("X-Sig", sig);
        ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(req, body, "X-Sig", secret)
            .Should().BeTrue();
    }

    [Fact]
    public void Hex_TamperedBody_Fails()
    {
        var secret = "shhh";
        var body = Encoding.UTF8.GetBytes("{\"id\":\"abc\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(body));
        var req = BuildRequest("X-Sig", sig);
        var tampered = Encoding.UTF8.GetBytes("{\"id\":\"xyz\"}");
        ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(req, tampered, "X-Sig", secret)
            .Should().BeFalse();
    }

    [Fact]
    public void Hex_MissingSecret_Fails()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var req = BuildRequest("X-Sig", "deadbeef");
        ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(req, body, "X-Sig", string.Empty)
            .Should().BeFalse();
    }

    [Fact]
    public void Hex_MissingHeader_Fails()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var ctx = new DefaultHttpContext();
        ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(ctx.Request, body, "X-Sig", "shhh")
            .Should().BeFalse();
    }

    [Fact]
    public void Hex_MalformedHex_Fails()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var req = BuildRequest("X-Sig", "not-hex");
        ProviderWebhookSignature.ValidateHexHmacSha256FromHeader(req, body, "X-Sig", "shhh")
            .Should().BeFalse();
    }

    [Fact]
    public void Base64_ValidSignature_Passes()
    {
        var secret = "shhh";
        var body = Encoding.UTF8.GetBytes("{\"id\":\"abc\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(body));
        var req = BuildRequest("X-Sig", sig);
        ProviderWebhookSignature.ValidateBase64HmacSha256FromHeader(req, body, "X-Sig", secret)
            .Should().BeTrue();
    }
}
