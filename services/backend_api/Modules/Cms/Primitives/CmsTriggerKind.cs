namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// 11 lifecycle-transition trigger kinds per spec 024 data-model §3 + tasks T018.
/// Recorded in audit rows on every transition.
/// </summary>
public static class CmsTriggerKind
{
    public const string EditorSave = "editor_save";
    public const string PublisherSchedule = "publisher_schedule";
    public const string PublisherPublishNow = "publisher_publish_now";
    public const string PublisherArchive = "publisher_archive";
    public const string PublisherSchedulePastStart = "publisher_schedule_past_start";
    public const string PublisherSchedulePastEffective = "publisher_schedule_past_effective";
    public const string WorkerPromoteToLive = "worker_promote_to_live";
    public const string WorkerPromoteToArchived = "worker_promote_to_archived";
    public const string WorkerSupersedeLegalVersion = "worker_supersede_legal_version";
    public const string SuperAdminForce = "super_admin_force";
    public const string EditorDeleteUnpublishedDraft = "editor_delete_unpublished_draft";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        EditorSave,
        PublisherSchedule,
        PublisherPublishNow,
        PublisherArchive,
        PublisherSchedulePastStart,
        PublisherSchedulePastEffective,
        WorkerPromoteToLive,
        WorkerPromoteToArchived,
        WorkerSupersedeLegalVersion,
        SuperAdminForce,
        EditorDeleteUnpublishedDraft,
    };
}
