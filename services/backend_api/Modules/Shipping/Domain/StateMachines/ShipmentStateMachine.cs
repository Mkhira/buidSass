using BackendApi.Modules.Shipping.Primitives;

namespace BackendApi.Modules.Shipping.Domain.StateMachines;

/// <summary>
/// Authoritative transition graph for <see cref="Shipment"/> per
/// <c>data-model.md</c> §state-machine. Used both at write paths
/// (admin actions, subscriber lifecycle) and at the webhook handler's
/// precedence-resolution step (BR-12).
/// </summary>
public static class ShipmentStateMachine
{
    /// <summary>Allowed transitions: <c>from → {to,…}</c>.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [ShipmentStates.Pending] = Set(
                ShipmentStates.LabelPurchased,
                ShipmentStates.FailedToCreateLabel,
                ShipmentStates.PendingLabelProviderFailure,
                ShipmentStates.LabelVoided),

            [ShipmentStates.LabelPurchased] = Set(
                ShipmentStates.HandedToCarrier,
                ShipmentStates.LabelVoided),

            [ShipmentStates.HandedToCarrier] = Set(
                ShipmentStates.InTransit,
                ShipmentStates.LabelVoided),

            [ShipmentStates.InTransit] = Set(
                ShipmentStates.OutForDelivery,
                ShipmentStates.DeliveryAttempted,
                ShipmentStates.Delivered,
                ShipmentStates.LabelVoided),

            [ShipmentStates.OutForDelivery] = Set(
                ShipmentStates.Delivered,
                ShipmentStates.DeliveryAttempted,
                ShipmentStates.LabelVoided),

            [ShipmentStates.DeliveryAttempted] = Set(
                ShipmentStates.OutForDelivery,
                ShipmentStates.Delivered,
                ShipmentStates.DeliveryAttempted,
                ShipmentStates.ReturnToSenderInitiated,
                ShipmentStates.LabelVoided),

            [ShipmentStates.ReturnToSenderInitiated] = Set(
                ShipmentStates.ReturnedToSender),

            [ShipmentStates.Delivered] = Set(
                ShipmentStates.DeliveryDisputed),

            [ShipmentStates.DeliveryDisputed] = Set(
                ShipmentStates.ReDeliveredPending,
                ShipmentStates.ClosedWithRefund),

            [ShipmentStates.ReDeliveredPending] = Set(
                ShipmentStates.ClosedWithRefund),

            // Failure paths — operator must escalate to DeadLetter.
            [ShipmentStates.FailedToCreateLabel] = Set(
                ShipmentStates.LabelPurchased,
                ShipmentStates.DeadLetterLabel,
                ShipmentStates.LabelVoided),

            [ShipmentStates.PendingLabelProviderFailure] = Set(
                ShipmentStates.LabelPurchased,
                ShipmentStates.DeadLetterLabel,
                ShipmentStates.LabelVoided),

            // Terminal states have no outgoing transitions.
            [ShipmentStates.ReturnedToSender] = Set(),
            [ShipmentStates.ClosedWithRefund] = Set(),
            [ShipmentStates.DeadLetterLabel] = Set(ShipmentStates.LabelPurchased),
            [ShipmentStates.LabelVoided] = Set(),
        };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var set) && set.Contains(to);

    public static IReadOnlySet<string> AllowedTo(string from) =>
        Transitions.TryGetValue(from, out var set) ? set : EmptySet;

    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Shipment state transition not allowed: '{from}' → '{to}'.");
        }
    }

    /// <summary>
    /// BR-12 precedence: an incoming webhook only advances state if the
    /// proposed state has strictly higher precedence than the current state.
    /// Returns <c>true</c> when the caller should apply <paramref name="proposed"/>;
    /// <c>false</c> when the event should be recorded as history only.
    /// </summary>
    public static bool ShouldApply(string current, string proposed)
    {
        var currentRank = ShipmentStates.PrecedenceOf(current);
        var proposedRank = ShipmentStates.PrecedenceOf(proposed);
        // Either side outside the precedence track (e.g., terminal/off-path)
        // — fall back to the strict transition graph.
        if (currentRank < 0 || proposedRank < 0)
        {
            return CanTransition(current, proposed);
        }
        return proposedRank > currentRank;
    }

    private static IReadOnlySet<string> Set(params string[] members) =>
        new HashSet<string>(members, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);
}
