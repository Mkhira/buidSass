using BackendApi.Modules.Notifications.Primitives;

namespace BackendApi.Modules.Notifications.Domain.StateMachines;

/// <summary>
/// Authoritative <see cref="TemplateVersion.State"/> transition graph per
/// <c>spec.md §template-lifecycle</c>:
/// <c>draft → in_review → published ↔ archived</c>. Reject + restart edges
/// (in_review → draft) are explicit; archived → published (un-archive) is
/// allowed for operator recovery.
/// </summary>
public static class TemplateVersionStateMachine
{
    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // draft → in_review (author submits) | archived (cleanup of stale drafts).
            [NotificationsConstants.TemplateVersionStates.Draft] = Set(
                NotificationsConstants.TemplateVersionStates.InReview,
                NotificationsConstants.TemplateVersionStates.Archived),

            // in_review → published (reviewer approves, V-1 publish gate passes) |
            // draft (reviewer rejects; round-trip with comment) | archived (cleanup).
            [NotificationsConstants.TemplateVersionStates.InReview] = Set(
                NotificationsConstants.TemplateVersionStates.Published,
                NotificationsConstants.TemplateVersionStates.Draft,
                NotificationsConstants.TemplateVersionStates.Archived),

            // published → archived (new version published; old auto-archives) |
            // (no direct path to draft — must create a new version).
            [NotificationsConstants.TemplateVersionStates.Published] = Set(
                NotificationsConstants.TemplateVersionStates.Archived),

            // archived → published (operator un-archive; rare recovery path).
            [NotificationsConstants.TemplateVersionStates.Archived] = Set(
                NotificationsConstants.TemplateVersionStates.Published),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"TemplateVersion state transition not allowed: '{from}' → '{to}'.");
        }
    }

    public static IReadOnlySet<string> AllowedNextStates(string from) =>
        Transitions.TryGetValue(from, out var set) ? set : Set();
}
