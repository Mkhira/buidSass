using BackendApi.Modules.Cart.Primitives;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Cart.Tests.Unit;

public sealed class CartTokenProviderTests
{
    private static CartTokenProvider NewProvider(string? secret = null, int? lifetimeDays = null)
    {
        var options = Options.Create(new CartOptions
        {
            TokenSecret = secret ?? "unit-test-secret-A-0123456789abcdef",
            TokenLifetimeDays = lifetimeDays ?? 30,
        });
        return new CartTokenProvider(options);
    }

    [Fact]
    public void Issue_ProducesTokenAndLookupHash()
    {
        var provider = NewProvider();
        var now = DateTimeOffset.UtcNow;

        var issued = provider.Issue(now);

        issued.Token.Should().NotBeNullOrWhiteSpace();
        issued.Hash.Should().HaveCount(32);
        issued.IssuedAt.Should().Be(now);
    }

    [Fact]
    public void TryDecode_ValidToken_ReturnsSameHashAsIssue()
    {
        var provider = NewProvider();
        var now = DateTimeOffset.UtcNow;
        var issued = provider.Issue(now);

        var ok = provider.TryDecode(issued.Token, now, out var hash);

        ok.Should().BeTrue();
        hash.Should().BeEquivalentTo(issued.Hash);
    }

    [Fact]
    public void TryDecode_WrongSecret_Rejects()
    {
        var issuer = NewProvider(secret: "secret-A-very-strong-0123456789ab");
        var verifier = NewProvider(secret: "secret-B-different-0123456789abcd");
        var now = DateTimeOffset.UtcNow;
        var issued = issuer.Issue(now);

        var ok = verifier.TryDecode(issued.Token, now, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryDecode_TamperedPayload_Rejects()
    {
        var provider = NewProvider();
        var now = DateTimeOffset.UtcNow;
        var issued = provider.Issue(now);

        var tampered = issued.Token[..^3] + (issued.Token[^1] == 'A' ? "BBB" : "AAA");

        var ok = provider.TryDecode(tampered, now, out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryDecode_Expired_Rejects()
    {
        var provider = NewProvider(lifetimeDays: 1);
        var issueTime = DateTimeOffset.UtcNow.AddDays(-2);
        var issued = provider.Issue(issueTime);

        var ok = provider.TryDecode(issued.Token, DateTimeOffset.UtcNow, out _);
        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-base64-%%%")]
    [InlineData("short")]
    public void TryDecode_GarbageInput_ReturnsFalse(string token)
    {
        var provider = NewProvider();

        var ok = provider.TryDecode(token, DateTimeOffset.UtcNow, out var hash);

        ok.Should().BeFalse();
        hash.Should().BeEmpty();
    }

    [Fact]
    public void HashForLookup_MatchesIssuedHash()
    {
        // HashForLookup is used when the token signature isn't yet verified (fast path for
        // anonymous cart row lookup). It must produce the same hash as Issue so that
        // a subsequent TryDecode lands on the same row.
        var provider = NewProvider();
        var issued = provider.Issue();

        var hash = provider.HashForLookup(issued.Token);

        hash.Should().BeEquivalentTo(issued.Hash);
    }

    [Fact]
    public void Issue_ProducesDistinctTokensAndHashesPerCall()
    {
        var provider = NewProvider();
        var a = provider.Issue();
        var b = provider.Issue();

        a.Token.Should().NotBe(b.Token);
        a.Hash.Should().NotBeEquivalentTo(b.Hash);
    }
}
