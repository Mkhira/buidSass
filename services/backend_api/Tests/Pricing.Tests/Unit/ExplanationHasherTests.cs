using BackendApi.Modules.Pricing.Primitives.Explanation;
using FluentAssertions;

namespace Pricing.Tests.Unit;

public sealed class ExplanationHasherTests
{
    [Fact]
    public void Hash_IsStable_AcrossKeyOrderings()
    {
        var a = new { b = 1, a = 2, c = new { y = "y", x = "x" } };
        var b = new { c = new { x = "x", y = "y" }, a = 2, b = 1 };

        var (hashA, _, _) = ExplanationHasher.Hash(a);
        var (hashB, _, _) = ExplanationHasher.Hash(b);
        hashA.Should().Be(hashB);
    }

    [Fact]
    public void Hash_ChangesWhenValueChanges()
    {
        var (h1, _, _) = ExplanationHasher.Hash(new { x = 1 });
        var (h2, _, _) = ExplanationHasher.Hash(new { x = 2 });
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_PreservesArrayOrder()
    {
        var (h1, _, _) = ExplanationHasher.Hash(new { items = new[] { "a", "b" } });
        var (h2, _, _) = ExplanationHasher.Hash(new { items = new[] { "b", "a" } });
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_Base64UrlNoPadding()
    {
        var (hash, _, _) = ExplanationHasher.Hash(new { x = 1 });
        hash.Should().NotContain("=");
        hash.Should().NotContain("+");
        hash.Should().NotContain("/");
    }
}
