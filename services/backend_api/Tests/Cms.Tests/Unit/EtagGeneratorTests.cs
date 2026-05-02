using BackendApi.Modules.Cms.Storefront;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class EtagGeneratorTests
{
    [Fact]
    public void Same_payload_yields_same_etag()
    {
        var p1 = new { items = new[] { new { id = 1, name = "a" } }, page = 1, total = 1 };
        var p2 = new { items = new[] { new { id = 1, name = "a" } }, page = 1, total = 1 };

        EtagGenerator.Compute(p1).Should().Be(EtagGenerator.Compute(p2));
    }

    [Fact]
    public void Different_payload_yields_different_etag()
    {
        var p1 = new { items = new[] { new { id = 1, name = "a" } } };
        var p2 = new { items = new[] { new { id = 2, name = "a" } } };

        EtagGenerator.Compute(p1).Should().NotBe(EtagGenerator.Compute(p2));
    }

    [Fact]
    public void Etag_is_weak_format()
    {
        var tag = EtagGenerator.Compute(new { ok = true });
        tag.Should().StartWith("W/\"").And.EndWith("\"");
    }

    [Fact]
    public void Null_payload_yields_stable_null_tag()
    {
        EtagGenerator.Compute<object?>(null).Should().Be(EtagGenerator.Compute<object?>(null));
    }
}
