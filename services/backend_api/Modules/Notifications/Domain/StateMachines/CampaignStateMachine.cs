using BackendApi.Modules.Notifications.Primitives;

namespace BackendApi.Modules.Notifications.Domain.StateMachines;

/// <summary>
/// Authoritative <see cref="Campaign.State"/> transition graph per
/// <c>spec.md §campaign-lifecycle</c>:
/// <c>draft → scheduled → sending → completed | paused → sending | cancelled</c>.
/// </summary>
public static class CampaignStateMachine
{
    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // draft → scheduled (admin schedules with send_at) | cancelled.
            [NotificationsConstants.CampaignStates.Draft] = Set(
                NotificationsConstants.CampaignStates.Scheduled,
                NotificationsConstants.CampaignStates.Cancelled),

            // scheduled → sending (send_at arrives) | cancelled (pre-send abort).
            [NotificationsConstants.CampaignStates.Scheduled] = Set(
                NotificationsConstants.CampaignStates.Sending,
                NotificationsConstants.CampaignStates.Cancelled),

            // sending → completed (all recipients dispatched) |
            // paused (admin pause) | cancelled (admin hard-stop mid-send).
            [NotificationsConstants.CampaignStates.Sending] = Set(
                NotificationsConstants.CampaignStates.Completed,
                NotificationsConstants.CampaignStates.Paused,
                NotificationsConstants.CampaignStates.Cancelled),

            // paused → sending (resume) | cancelled.
            [NotificationsConstants.CampaignStates.Paused] = Set(
                NotificationsConstants.CampaignStates.Sending,
                NotificationsConstants.CampaignStates.Cancelled),

            // Terminal states.
            [NotificationsConstants.CampaignStates.Completed] = Set(),
            [NotificationsConstants.CampaignStates.Cancelled] = Set(),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Campaign state transition not allowed: '{from}' → '{to}'.");
        }
    }

    public static IReadOnlySet<string> AllowedNextStates(string from) =>
        Transitions.TryGetValue(from, out var set) ? set : Set();
}
