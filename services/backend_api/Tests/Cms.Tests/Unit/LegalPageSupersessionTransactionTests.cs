using BackendApi.Modules.Cms.LegalOwner;
using BackendApi.Modules.Cms.Primitives;
using FluentAssertions;

namespace Cms.Tests.Unit;

/// <summary>
/// Pure-logic coverage for the LegalPageSupersessionTransaction's helpers.
/// The full Postgres-backed transaction (FOR UPDATE + advisory lock + partial
/// unique index race) is exercised by the integration suite in
/// Tests/Cms.Tests/Integration/LegalOwner/* once the WebApplicationFactory
/// + Testcontainers harness lands; here we lock down the lock-key derivation
/// (must be stable across runs and identical for the same (kind, market))
/// and the worker-trigger transitions used by the supersession path.
/// </summary>
public sealed class LegalPageSupersessionTransactionTests
{
    [Fact]
    public void HashLockKey_is_stable_across_invocations()
    {
        var k1 = LegalPageSupersessionTransaction.HashLockKey("privacy", "EG");
        var k2 = LegalPageSupersessionTransaction.HashLockKey("privacy", "EG");
        k1.Should().Be(k2);
    }

    [Theory]
    [InlineData("privacy", "EG", "privacy", "KSA")]
    [InlineData("terms", "EG", "privacy", "EG")]
    [InlineData("privacy", "*", "privacy", "EG")]
    public void HashLockKey_differs_for_different_pairs(string k1, string m1, string k2, string m2)
    {
        var a = LegalPageSupersessionTransaction.HashLockKey(k1, m1);
        var b = LegalPageSupersessionTransaction.HashLockKey(k2, m2);
        a.Should().NotBe(b);
    }

    [Fact]
    public void HashLockKey_uses_the_legal_page_prefix()
    {
        var (prefix, _) = LegalPageSupersessionTransaction.HashLockKey("privacy", "EG");
        // 0x024_C5_00 = 2_279_680.
        prefix.Should().Be(0x024_C5_00);
    }

    [Fact]
    public void Live_to_superseded_is_legal_for_legal_page_versions()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Superseded,
            EntityKind.LegalPageVersion,
            CmsTriggerKind.WorkerSupersedeLegalVersion, CmsActorKind.System,
            out _);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Live_to_superseded_is_forbidden_for_non_legal_kinds()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Superseded,
            EntityKind.BannerSlot,
            CmsTriggerKind.WorkerSupersedeLegalVersion, CmsActorKind.System,
            out var reason);
        ok.Should().BeFalse();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void Worker_promote_to_live_is_legal_for_legal_page_version()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Scheduled, ContentLifecycleState.Live,
            EntityKind.LegalPageVersion,
            CmsTriggerKind.WorkerPromoteToLive, CmsActorKind.System,
            out _);
        ok.Should().BeTrue();
    }

    [Fact]
    public void LocaleGate_blocks_when_effective_at_missing()
    {
        var result = LocaleCompletenessGate.CheckLegalPageVersion(
            "ar body", "en body", effectiveAtUtc: null);
        result.IsAllowed.Should().BeFalse();
        result.MissingFields.Should().Contain("effective_at_utc");
    }

    [Fact]
    public void LocaleGate_blocks_when_either_body_missing()
    {
        var ar = LocaleCompletenessGate.CheckLegalPageVersion(
            null, "en", DateTimeOffset.UtcNow);
        ar.IsAllowed.Should().BeFalse();
        ar.MissingFields.Should().Contain("body_ar");

        var en = LocaleCompletenessGate.CheckLegalPageVersion(
            "ar", null, DateTimeOffset.UtcNow);
        en.IsAllowed.Should().BeFalse();
        en.MissingFields.Should().Contain("body_en");
    }

    [Fact]
    public void LocaleGate_passes_with_both_bodies_and_effective_at()
    {
        var result = LocaleCompletenessGate.CheckLegalPageVersion(
            "ar", "en", DateTimeOffset.UtcNow);
        result.IsAllowed.Should().BeTrue();
        result.MissingFields.Should().BeEmpty();
    }
}
