using BackendApi.Modules.Notifications.Primitives;

namespace BackendApi.Modules.Notifications.Domain.StateMachines;

/// <summary>
/// Authoritative <see cref="Notification.State"/> transition graph per
/// <c>spec.md §state-machine</c>. Used at every write path (subscriber
/// enqueue, worker dispatch, webhook handler, operator retry, dead-letter
/// archiver). Failing transitions raise <see cref="InvalidOperationException"/>
/// — there is no soft-fail path.
/// </summary>
public static class NotificationStateMachine
{
    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // pending → queued (worker pickup), skipped (pre-flight gate fails),
            // or failed (template-render error).
            [NotificationsConstants.NotificationStates.Pending] = Set(
                NotificationsConstants.NotificationStates.Queued,
                NotificationsConstants.NotificationStates.Skipped,
                NotificationsConstants.NotificationStates.Failed),

            // queued → sending (provider call begins) | skipped (rate-limit
            // tripped between enqueue and dispatch).
            [NotificationsConstants.NotificationStates.Queued] = Set(
                NotificationsConstants.NotificationStates.Sending,
                NotificationsConstants.NotificationStates.Skipped),

            // sending → delivered (success / accepted by provider) | failed
            // (terminal 4xx) | retrying (transient 5xx / network).
            [NotificationsConstants.NotificationStates.Sending] = Set(
                NotificationsConstants.NotificationStates.Delivered,
                NotificationsConstants.NotificationStates.Failed,
                NotificationsConstants.NotificationStates.Retrying),

            // retrying → sending (next attempt) | dead_letter (retry budget
            // exhausted) | failed (terminal classification on retry).
            [NotificationsConstants.NotificationStates.Retrying] = Set(
                NotificationsConstants.NotificationStates.Sending,
                NotificationsConstants.NotificationStates.DeadLetter,
                NotificationsConstants.NotificationStates.Failed),

            // failed → retrying (operator-initiated retry from failed; rare) |
            // dead_letter (auto-roll on budget exhaustion).
            [NotificationsConstants.NotificationStates.Failed] = Set(
                NotificationsConstants.NotificationStates.Retrying,
                NotificationsConstants.NotificationStates.DeadLetter),

            // dead_letter → pending (operator "Retry now" resets the loop) |
            // skipped (operator "Discard" terminal flag).
            [NotificationsConstants.NotificationStates.DeadLetter] = Set(
                NotificationsConstants.NotificationStates.Pending,
                NotificationsConstants.NotificationStates.Skipped),

            // Terminal states.
            [NotificationsConstants.NotificationStates.Delivered] = Set(),
            [NotificationsConstants.NotificationStates.Skipped] = Set(),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Notification state transition not allowed: '{from}' → '{to}'.");
        }
    }

    /// <summary>Returns all permitted "to" states for the given current state.</summary>
    public static IReadOnlySet<string> AllowedNextStates(string from) =>
        Transitions.TryGetValue(from, out var set) ? set : Set();
}
