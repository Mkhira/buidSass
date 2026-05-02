using System.Reflection;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;

namespace Verification.Tests.Integration;

/// <summary>
/// Spec 020 T049 / FR-031 + FR-032 + FR-033 — locale-completeness contract
/// for customer-side verification responses.
///
/// <para><b>What this asserts.</b> The verification module surfaces error
/// reason codes via the wire <c>reasonCode</c> field; the customer-facing
/// client localizes via the per-locale ICU bundle keyed by that code.
/// Therefore every code that any customer-facing endpoint can emit MUST
/// have a corresponding entry in BOTH <c>verification.en.icu</c> and
/// <c>verification.ar.icu</c> — otherwise an Arabic customer sees a
/// missing-key fallback and the bilingual contract (Principle 4) breaks.</para>
///
/// <para><b>What this does NOT assert.</b> The Problem Details
/// <c>title</c>/<c>detail</c> fields surface English-only diagnostic strings
/// for the wire envelope; client UIs render the localized message via the
/// ICU bundle. This is intentional — server-side localization based on
/// <c>Accept-Language</c> is deferred to spec 025 (Notifications) per the
/// data-model §6 trade-off.</para>
///
/// <para>The companion sibling test
/// <c>EligibilityReasonCodeIcuKeysTests</c> covers the eligibility-side
/// reason code surface; this test covers the verification-handler reason
/// code surface so any new code introduced by a future slice forces a
/// matching pair of bundle entries before the slice can ship at DoD.</para>
/// </summary>
public sealed class CustomerSubmissionLocaleTests
{
    [Fact]
    public void Every_VerificationReasonCode_has_an_entry_in_both_AR_and_EN_bundles()
    {
        var enBundle = LoadBundle("verification.en.icu");
        var arBundle = LoadBundle("verification.ar.icu");

        var allCodes = Enum.GetValues<VerificationReasonCode>();
        var missingEn = new List<string>();
        var missingAr = new List<string>();

        foreach (var code in allCodes)
        {
            var key = code.ToIcuKey();
            if (!enBundle.ContainsKey(key)) missingEn.Add(key);
            if (!arBundle.ContainsKey(key)) missingAr.Add(key);
        }

        // Single combined assertion so a CI failure surfaces every missing
        // key at once (better signal than failing on the first miss).
        var missing = new List<string>();
        if (missingEn.Count > 0) missing.Add($"EN missing: {string.Join(", ", missingEn)}");
        if (missingAr.Count > 0) missing.Add($"AR missing: {string.Join(", ", missingAr)}");

        missing.Should().BeEmpty(
            "every VerificationReasonCode emitted by a handler MUST have a key in BOTH locale bundles before the slice ships at DoD (Principle 4 / SC-008 / FR-031 + FR-032)");
    }

    [Fact]
    public void Bundles_are_locale_distinct_so_AR_is_NOT_machine_translated_from_EN()
    {
        // Sanity: every key that exists in both bundles MUST have different
        // values across locales. A copy/paste of the EN string into the AR
        // bundle is a Principle-4 violation (no machine-translated AR).
        var enBundle = LoadBundle("verification.en.icu");
        var arBundle = LoadBundle("verification.ar.icu");

        var sharedKeys = enBundle.Keys.Intersect(arBundle.Keys).ToList();
        sharedKeys.Should().NotBeEmpty(
            "the bundles must share at least one key for this property to be meaningful");

        var copyPasted = sharedKeys
            .Where(k => string.Equals(enBundle[k], arBundle[k], StringComparison.Ordinal))
            // Exclude trivial/empty values from the diff if any sneak through.
            .Where(k => !string.IsNullOrWhiteSpace(enBundle[k]))
            .ToList();

        copyPasted.Should().BeEmpty(
            "AR strings MUST be human-authored, not copied verbatim from EN (Principle 4 / SC-008)");
    }

    private static IReadOnlyDictionary<string, string> LoadBundle(string fileName)
    {
        // Locate the messages bundle relative to the test runtime. The bundles
        // live alongside the module source — walk up from the test bin/Debug
        // path until we find the repo, then dive into the verification module.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Modules/Verification/Messages", fileName)))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new FileNotFoundException(
                $"Could not locate verification messages bundle '{fileName}' walking up from {AppContext.BaseDirectory}.");
        }

        var path = Path.Combine(dir.FullName, "Modules/Verification/Messages", fileName);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var idx = line.IndexOf('=');
            if (idx < 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }
}
