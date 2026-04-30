using BackendApi.Modules.Reviews.Filtering;
using FluentAssertions;

namespace Reviews.Tests.Unit.Filtering;

/// <summary>Spec 022 T049 — media-attachment detection per FR-014a.</summary>
public sealed class MediaAttachmentDetectorTests
{
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "https://storage.test/abc" }, true)]
    [InlineData(new[] { "https://storage.test/a", "https://storage.test/b" }, true)]
    [InlineData(new[] { "" }, false)]
    [InlineData(new[] { "   " }, false)]
    public void Enumerable_overload_matches_table(string[] urls, bool expected)
    {
        MediaAttachmentDetector.HasMedia(urls).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("[]", false)]
    [InlineData("[\"\"]", false)]
    [InlineData("[\"https://storage.test/abc\"]", true)]
    [InlineData("[\"a\", \"b\"]", true)]
    [InlineData("invalid json", false)] // graceful fail-closed on parse error
    public void Json_overload_matches_table(string? json, bool expected)
    {
        MediaAttachmentDetector.HasMedia(json).Should().Be(expected);
    }
}
