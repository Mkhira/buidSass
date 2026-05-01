namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Pure-function state-machine guard for the unified CMS content lifecycle.
/// Encodes the transition table from data-model.md §3 — including the
/// legal-page <c>Live → Superseded</c> edge — and rejects every other
/// transition with the kind-scoped <c>cms.{kind}.illegal_transition</c>
/// reason code via <see cref="InvalidContentTransitionException"/>.
/// </summary>
public static class CmsContentLifecycle
{
    /// <summary>
    /// Validates a proposed transition. Idempotent (from == to) is treated as legal
    /// no-op; the caller is responsible for short-circuiting before writing audit.
    /// </summary>
    public static bool TryTransition(
        ContentLifecycleState from,
        ContentLifecycleState to,
        EntityKind kind,
        string trigger,
        CmsActorKind actor,
        out string? reasonCode)
    {
        reasonCode = null;

        if (from == to)
        {
            return true;
        }

        // Terminal soft-delete + supersede are absolute terminals.
        if (from == ContentLifecycleState.Archived || from == ContentLifecycleState.Superseded)
        {
            reasonCode = CmsReasonCode.IllegalTransition(kind);
            return false;
        }

        // Superseded is reserved for legal page versions only.
        if (to == ContentLifecycleState.Superseded && kind != EntityKind.LegalPageVersion)
        {
            reasonCode = CmsReasonCode.IllegalTransition(kind);
            return false;
        }

        // Archived is forbidden for legal-page versions (use Superseded instead).
        if (to == ContentLifecycleState.Archived && kind == EntityKind.LegalPageVersion)
        {
            reasonCode = CmsReasonCode.IllegalTransition(kind);
            return false;
        }

        if (!CmsTriggerKind.All.Contains(trigger))
        {
            reasonCode = CmsReasonCode.IllegalTransition(kind);
            return false;
        }

        switch ((from, to))
        {
            // draft → scheduled
            case (ContentLifecycleState.Draft, ContentLifecycleState.Scheduled)
                when trigger == CmsTriggerKind.PublisherSchedule
                  && (actor == CmsActorKind.Publisher
                   || actor == CmsActorKind.LegalOwner
                   || actor == CmsActorKind.SuperAdmin):

            // draft → live
            case (ContentLifecycleState.Draft, ContentLifecycleState.Live)
                when trigger == CmsTriggerKind.PublisherPublishNow
                  && (actor == CmsActorKind.Publisher
                   || actor == CmsActorKind.LegalOwner
                   || actor == CmsActorKind.SuperAdmin):

            // scheduled → live
            case (ContentLifecycleState.Scheduled, ContentLifecycleState.Live)
                when trigger == CmsTriggerKind.WorkerPromoteToLive
                  && actor == CmsActorKind.System:

            // live → archived (publisher / legal_owner)
            case (ContentLifecycleState.Live, ContentLifecycleState.Archived)
                when trigger == CmsTriggerKind.PublisherArchive
                  && (actor == CmsActorKind.Publisher
                   || actor == CmsActorKind.LegalOwner
                   || actor == CmsActorKind.SuperAdmin):

            // live → archived (worker, banner only on scheduled_end_utc)
            case (ContentLifecycleState.Live, ContentLifecycleState.Archived)
                when trigger == CmsTriggerKind.WorkerPromoteToArchived
                  && actor == CmsActorKind.System
                  && kind == EntityKind.BannerSlot:

            // live → superseded (legal_page_version only, system-driven)
            case (ContentLifecycleState.Live, ContentLifecycleState.Superseded)
                when trigger == CmsTriggerKind.WorkerSupersedeLegalVersion
                  && actor == CmsActorKind.System
                  && kind == EntityKind.LegalPageVersion:

            // super_admin force overrides for ops emergencies (audited)
            case (_, _)
                when trigger == CmsTriggerKind.SuperAdminForce
                  && actor == CmsActorKind.SuperAdmin
                  && IsForceTargetLegal(from, to, kind):
                return true;

            default:
                reasonCode = CmsReasonCode.IllegalTransition(kind);
                return false;
        }
    }

    /// <summary>Throws <see cref="InvalidContentTransitionException"/> when the proposed transition is forbidden.</summary>
    public static void AssertCanTransition(
        ContentLifecycleState from,
        ContentLifecycleState to,
        EntityKind kind,
        string trigger,
        CmsActorKind actor)
    {
        if (!TryTransition(from, to, kind, trigger, actor, out var reason))
        {
            throw new InvalidContentTransitionException(from, to, kind, reason!);
        }
    }

    /// <summary>True for terminal states.</summary>
    public static bool IsTerminal(ContentLifecycleState state) =>
        state == ContentLifecycleState.Archived || state == ContentLifecycleState.Superseded;

    private static bool IsForceTargetLegal(ContentLifecycleState from, ContentLifecycleState to, EntityKind kind)
    {
        // super_admin can force any non-terminal source to scheduled / live / archived;
        // superseded remains restricted to legal-page versions, archived to non-legal.
        if (from is ContentLifecycleState.Archived or ContentLifecycleState.Superseded) return false;
        if (to == ContentLifecycleState.Superseded) return kind == EntityKind.LegalPageVersion;
        if (to == ContentLifecycleState.Archived) return kind != EntityKind.LegalPageVersion;
        return to is ContentLifecycleState.Draft
            or ContentLifecycleState.Scheduled
            or ContentLifecycleState.Live;
    }
}

/// <summary>Thrown by <see cref="CmsContentLifecycle.AssertCanTransition"/> on a forbidden transition.</summary>
public sealed class InvalidContentTransitionException(
    ContentLifecycleState from,
    ContentLifecycleState to,
    EntityKind kind,
    string reasonCode)
    : Exception($"Forbidden CMS lifecycle transition for {kind}: {from} → {to} ({reasonCode}).")
{
    public ContentLifecycleState From { get; } = from;
    public ContentLifecycleState To { get; } = to;
    public EntityKind Kind { get; } = kind;
    public string ReasonCode { get; } = reasonCode;
}
