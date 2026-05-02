using BackendApi.Modules.Cms.Primitives;
using FluentAssertions;

namespace Cms.Tests.Unit;

public sealed class CmsContentLifecycleTests
{
    [Fact]
    public void Publisher_can_schedule_a_draft()
    {
        CmsContentLifecycle
            .TryTransition(
                ContentLifecycleState.Draft, ContentLifecycleState.Scheduled,
                EntityKind.BannerSlot, CmsTriggerKind.PublisherSchedule, CmsActorKind.Publisher,
                out var reason)
            .Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void Worker_promotes_scheduled_to_live()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Scheduled, ContentLifecycleState.Live,
            EntityKind.BannerSlot, CmsTriggerKind.WorkerPromoteToLive, CmsActorKind.System,
            out _);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Worker_archives_live_banner_at_scheduled_end()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Archived,
            EntityKind.BannerSlot, CmsTriggerKind.WorkerPromoteToArchived, CmsActorKind.System,
            out _);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Worker_cannot_archive_legal_page_versions()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Archived,
            EntityKind.LegalPageVersion, CmsTriggerKind.WorkerPromoteToArchived, CmsActorKind.System,
            out var reason);
        ok.Should().BeFalse();
        reason.Should().Be(CmsReasonCode.IllegalTransition(EntityKind.LegalPageVersion));
    }

    [Fact]
    public void Live_to_superseded_only_for_legal_page_versions()
    {
        CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Superseded,
            EntityKind.LegalPageVersion, CmsTriggerKind.WorkerSupersedeLegalVersion, CmsActorKind.System,
            out _).Should().BeTrue();

        CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Superseded,
            EntityKind.BannerSlot, CmsTriggerKind.WorkerSupersedeLegalVersion, CmsActorKind.System,
            out var reason).Should().BeFalse();
        reason.Should().Be(CmsReasonCode.IllegalTransition(EntityKind.BannerSlot));
    }

    [Fact]
    public void Archived_is_terminal()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Archived, ContentLifecycleState.Live,
            EntityKind.BannerSlot, CmsTriggerKind.SuperAdminForce, CmsActorKind.SuperAdmin,
            out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Idempotent_self_transition_allowed()
    {
        CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Live, ContentLifecycleState.Live,
            EntityKind.BannerSlot, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Publisher,
            out _).Should().BeTrue();
    }

    [Fact]
    public void Editor_cannot_publish_directly()
    {
        var ok = CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Draft, ContentLifecycleState.Live,
            EntityKind.BannerSlot, CmsTriggerKind.PublisherPublishNow, CmsActorKind.Editor,
            out var reason);
        ok.Should().BeFalse();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void Super_admin_force_can_jump_states_for_ops()
    {
        CmsContentLifecycle.TryTransition(
            ContentLifecycleState.Draft, ContentLifecycleState.Live,
            EntityKind.BannerSlot, CmsTriggerKind.SuperAdminForce, CmsActorKind.SuperAdmin,
            out _).Should().BeTrue();
    }
}
