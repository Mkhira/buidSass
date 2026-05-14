using System.Text.Json;
using BackendApi.Modules.Support.Primitives;
using FluentAssertions;

namespace Support.Tests.Unit;

/// <summary>
/// Spec 023 T143 / T144 — ICU resource files MUST cover every reason code
/// owned by the Support module. This invariant test (T144 hard-pre-merge
/// gate for spec 014 / 015 PRs) loads <c>support.en.icu</c> +
/// <c>support.ar.icu</c> from the module's output directory and asserts:
///   * Every <see cref="TicketReasonCode.All"/> entry has a string entry in
///     BOTH locales.
///   * Every key in the EN bundle also exists in the AR bundle (no orphans).
///   * The AR bundle declares <c>editorial_status</c> in its <c>_meta</c>
///     block so the consumer-PR gate (014 / 015) can detect unreviewed
///     strings before merging UI that surfaces them.
/// </summary>
public sealed class TicketReasonCodeIcuKeyCompletenessTests
{
    private static readonly string IcuRoot = ResolveIcuRoot();

    [Fact]
    public void Every_TicketReasonCode_has_an_entry_in_both_locales()
    {
        var en = LoadIcuBundle("support.en.icu");
        var ar = LoadIcuBundle("support.ar.icu");

        foreach (var code in TicketReasonCode.All)
        {
            en.Should().ContainKey(code,
                $"EN bundle must declare a string for {code}");
            ar.Should().ContainKey(code,
                $"AR bundle must declare a string for {code}");
        }
    }

    [Fact]
    public void Every_EN_key_has_a_matching_AR_key()
    {
        var en = LoadIcuBundle("support.en.icu");
        var ar = LoadIcuBundle("support.ar.icu");

        foreach (var key in en.Keys)
        {
            if (key == "_meta") continue;
            ar.Should().ContainKey(key,
                $"AR bundle is missing key '{key}' present in the EN bundle");
        }
    }

    [Fact]
    public void Ar_bundle_meta_declares_editorial_status_for_consumer_pr_gate()
    {
        var path = Path.Combine(IcuRoot, "support.ar.icu");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var meta = doc.RootElement.GetProperty("_meta");
        meta.TryGetProperty("editorial_status", out var status).Should().BeTrue(
            "consumer-PR gate (spec 014 / 015) reads _meta.editorial_status to block " +
            "merges that surface unreviewed AR strings to production-bound surfaces");
        status.GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static IReadOnlyDictionary<string, string> LoadIcuBundle(string fileName)
    {
        var path = Path.Combine(IcuRoot, fileName);
        File.Exists(path).Should().BeTrue($"ICU bundle missing at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "_meta") continue;
            if (prop.Value.ValueKind != JsonValueKind.String) continue;
            entries[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return entries;
    }

    private static string ResolveIcuRoot()
    {
        // The .csproj copies Modules/Support/Messages/*.icu to the test output
        // directory under the same relative path.
        var assemblyDir = Path.GetDirectoryName(typeof(TicketReasonCodeIcuKeyCompletenessTests).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot resolve assembly directory.");
        var primary = Path.Combine(assemblyDir, "Modules", "Support", "Messages");
        if (Directory.Exists(primary)) return primary;
        // Fallback: walk up to repo root then drop in the canonical source path.
        var dir = new DirectoryInfo(assemblyDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Modules", "Support", "Messages");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Modules/Support/Messages relative to the test assembly.");
    }
}
