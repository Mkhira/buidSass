using System.Reflection;
using System.Text.Json;
using BackendApi.Modules.B2B.Primitives;
using FluentAssertions;

namespace B2B.Tests.Unit;

/// <summary>
/// Spec 021 T018 — every <see cref="QuoteReasonCode"/> enum value MUST resolve to a
/// non-empty entry in BOTH <c>b2b.en.icu</c> and <c>b2b.ar.icu</c>.
///
/// Loads the ICU bundles directly from disk so the test fails on any merge that adds a
/// new enum value without the matching ICU keys (Principle 4 quality bar).
/// </summary>
public sealed class QuoteReasonCodeIcuKeysTests
{
    private static string LocateMessagesDir()
    {
        // Walk up from the test assembly until we find the Modules/B2B/Messages folder.
        var current = new DirectoryInfo(Path.GetDirectoryName(typeof(QuoteReasonCodeIcuKeysTests).Assembly.Location)!);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Modules", "B2B", "Messages");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Modules/B2B/Messages relative to test assembly. " +
            "If the project layout moved, update LocateMessagesDir().");
    }

    private static IDictionary<string, string> LoadIcu(string filename)
    {
        var path = Path.Combine(LocateMessagesDir(), filename);
        File.Exists(path).Should().BeTrue($"ICU bundle {filename} must exist");
        var raw = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(raw);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // Skip the _meta block; only string values are bundle entries.
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return dict;
    }

    [Fact]
    public void Every_quote_reason_code_has_non_empty_en_and_ar_keys()
    {
        var en = LoadIcu("b2b.en.icu");
        var ar = LoadIcu("b2b.ar.icu");

        var missing = new List<string>();
        foreach (QuoteReasonCode code in Enum.GetValues<QuoteReasonCode>())
        {
            var key = code.ToIcuKey();
            if (!en.TryGetValue(key, out var enValue) || string.IsNullOrWhiteSpace(enValue))
            {
                missing.Add($"{key} missing or empty in en");
            }
            if (!ar.TryGetValue(key, out var arValue) || string.IsNullOrWhiteSpace(arValue))
            {
                missing.Add($"{key} missing or empty in ar");
            }
        }

        missing.Should().BeEmpty(
            "every QuoteReasonCode MUST resolve to non-empty en+ar entries (Principle 4)");
    }
}
