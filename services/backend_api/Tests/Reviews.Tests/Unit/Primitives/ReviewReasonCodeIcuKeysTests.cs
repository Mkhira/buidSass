using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;

namespace Reviews.Tests.Unit.Primitives;

/// <summary>
/// Spec 022 T047 — every owned reason code from <see cref="ReviewReasonCode.All"/>
/// has a non-empty key in BOTH <c>reviews.en.icu</c> and <c>reviews.ar.icu</c>.
/// Editorial-grade AR review remains a separate gate (T142 / AR_EDITORIAL_REVIEW.md);
/// this test only enforces presence + non-emptiness.
/// </summary>
public sealed class ReviewReasonCodeIcuKeysTests
{
    private static readonly string MessagesDir = ResolveMessagesDir();

    [Fact]
    public void English_dictionary_covers_every_reason_code()
    {
        var dict = LoadDict(Path.Combine(MessagesDir, "reviews.en.icu"));
        foreach (var code in ReviewReasonCode.All)
        {
            dict.TryGetValue(code, out var value).Should().BeTrue($"reviews.en.icu must contain key '{code}'");
            value.Should().NotBeNullOrWhiteSpace($"reviews.en.icu key '{code}' must have a non-empty value");
        }
    }

    [Fact]
    public void Arabic_dictionary_covers_every_reason_code()
    {
        var dict = LoadDict(Path.Combine(MessagesDir, "reviews.ar.icu"));
        foreach (var code in ReviewReasonCode.All)
        {
            dict.TryGetValue(code, out var value).Should().BeTrue($"reviews.ar.icu must contain key '{code}'");
            value.Should().NotBeNullOrWhiteSpace($"reviews.ar.icu key '{code}' must have a non-empty value");
        }
    }

    [Fact]
    public void Editorial_review_table_lists_every_reason_code()
    {
        var path = Path.Combine(MessagesDir, "AR_EDITORIAL_REVIEW.md");
        var content = File.ReadAllText(path);
        foreach (var code in ReviewReasonCode.All)
        {
            content.Should().Contain($"| {code} |", $"AR_EDITORIAL_REVIEW.md must track sign-off for '{code}'");
        }
    }

    private static IReadOnlyDictionary<string, string> LoadDict(string path)
    {
        File.Exists(path).Should().BeTrue($"Expected ICU file at {path}");
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimStart();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eqIndex = raw.IndexOf('=');
            if (eqIndex < 1) continue;

            var key = raw[..eqIndex].Trim();
            var value = raw[(eqIndex + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }

    private static string ResolveMessagesDir()
    {
        // Tests run from .../bin/Debug/net9.0/. Walk up until we find the repo's
        // Modules/Reviews/Messages directory.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "Modules", "Reviews", "Messages");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        // Repo-root traversal (Tests/Reviews.Tests/bin/Debug/net9.0 → services/backend_api/Modules/Reviews/Messages)
        var repo = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(repo, "services", "backend_api", "Modules", "Reviews", "Messages");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(repo)?.FullName;
            if (parent == null || parent == repo) break;
            repo = parent;
        }
        throw new DirectoryNotFoundException("Could not locate Modules/Reviews/Messages from test base dir.");
    }
}
