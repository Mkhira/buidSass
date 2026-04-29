using System.Reflection;
using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;

namespace Verification.Tests.Unit;

/// <summary>
/// Spec 020 SC-008 / Principle 4 gate: every <see cref="EligibilityReasonCode"/> AND
/// every <see cref="VerificationReasonCode"/> MUST have an ICU key in BOTH
/// <c>verification.en.icu</c> AND <c>verification.ar.icu</c> bundles before the
/// slice that emits the code can ship at DoD.
/// </summary>
/// <remarks>
/// In Phase 2 the bundles are still empty (only the header comments). This test
/// is INFRASTRUCTURE-LEVEL ONLY at this point — it asserts the bundle FILES exist
/// and that the mapper does not throw on any enum value. Per-key bundle coverage
/// is enforced once Phase 3 lands the first batch of customer-visible reason
/// codes; the test will be tightened then. This minimal version protects against
/// dropped enum entries (every reason code MUST resolve to a non-empty key path
/// via the mapper).
/// </remarks>
public sealed class EligibilityReasonCodeIcuKeysTests
{
    private static readonly string MessagesDir = ResolveMessagesDir();

    [Fact]
    public void Both_locale_bundles_exist()
    {
        File.Exists(Path.Combine(MessagesDir, "verification.en.icu"))
            .Should().BeTrue($"verification.en.icu MUST exist under {MessagesDir}");
        File.Exists(Path.Combine(MessagesDir, "verification.ar.icu"))
            .Should().BeTrue($"verification.ar.icu MUST exist under {MessagesDir}");
    }

    [Fact]
    public void Every_eligibility_reason_code_has_a_resolvable_icu_key()
    {
        foreach (var code in Enum.GetValues<EligibilityReasonCode>())
        {
            var key = code.ToIcuKey();
            key.Should().NotBeNullOrWhiteSpace($"{code} MUST map to an ICU key");
            key.Should().StartWith("verification.eligibility.",
                $"{code} key '{key}' MUST be in the eligibility namespace");
        }
    }

    [Fact]
    public void Every_verification_reason_code_has_a_resolvable_icu_key()
    {
        foreach (var code in Enum.GetValues<VerificationReasonCode>())
        {
            var key = code.ToIcuKey();
            key.Should().NotBeNullOrWhiteSpace($"{code} MUST map to an ICU key");
            key.Should().StartWith("verification.",
                $"{code} key '{key}' MUST be in the verification namespace");
        }
    }

    [Fact]
    public void No_two_eligibility_reason_codes_share_the_same_icu_key()
    {
        var keys = Enum.GetValues<EligibilityReasonCode>()
            .Select(c => c.ToIcuKey())
            .ToList();

        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_two_verification_reason_codes_share_the_same_icu_key()
    {
        var keys = Enum.GetValues<VerificationReasonCode>()
            .Select(c => c.ToIcuKey())
            .ToList();

        keys.Should().OnlyHaveUniqueItems();
    }

    private static string ResolveMessagesDir()
    {
        // Walk up from the test assembly's BaseDirectory to find the repo root,
        // then dive into the module's Messages folder. The test binary lives at
        //   services/backend_api/Tests/Verification.Tests/bin/Debug/net9.0/
        // so we ascend 5 levels to reach repo root.
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null && dir.Name != "Verification.Tests")
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate Verification.Tests ancestor from base dir '{baseDir}'.");
        }

        // dir = .../Tests/Verification.Tests
        var moduleMessages = Path.GetFullPath(Path.Combine(
            dir.FullName, "..", "..", "Modules", "Verification", "Messages"));

        return moduleMessages;
    }
}
