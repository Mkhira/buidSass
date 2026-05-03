using BackendApi.Modules.B2B.Primitives;
using FluentAssertions;

namespace B2B.Tests.Unit;

/// <summary>
/// Spec 021 T015 — verifies every allowed and forbidden transition in
/// data-model §3.1, including the explicit "forbidden" cases:
/// terminal → non-terminal, requested → accepted, drafted → pending-approver.
/// </summary>
public sealed class QuoteStateMachineTests
{
    public static TheoryData<QuoteState, QuoteState, QuoteActorKind> AllowedTransitions => new()
    {
        // Admin opens requested to author.
        { QuoteState.Requested, QuoteState.Drafted, QuoteActorKind.AdminOperator },
        // Admin publishes draft.
        { QuoteState.Drafted, QuoteState.Revised, QuoteActorKind.AdminOperator },
        // Customer / buyer requests revision.
        { QuoteState.Revised, QuoteState.Drafted, QuoteActorKind.Customer },
        { QuoteState.Revised, QuoteState.Drafted, QuoteActorKind.Buyer },
        // Admin proactively re-opens to revise.
        { QuoteState.Revised, QuoteState.Drafted, QuoteActorKind.AdminOperator },
        // Buyer accepts → routed to approver.
        { QuoteState.Revised, QuoteState.PendingApprover, QuoteActorKind.Buyer },
        // Buyer / customer accepts when no approver gating.
        { QuoteState.Revised, QuoteState.Accepted, QuoteActorKind.Buyer },
        { QuoteState.Revised, QuoteState.Accepted, QuoteActorKind.Customer },
        // Approver finalizes / rejects.
        { QuoteState.PendingApprover, QuoteState.Accepted, QuoteActorKind.Approver },
        { QuoteState.PendingApprover, QuoteState.Revised, QuoteActorKind.Approver },
        // System rolls back when only-approver leaves.
        { QuoteState.PendingApprover, QuoteState.Revised, QuoteActorKind.System },
        // Customer rejects / withdraws from any non-terminal.
        { QuoteState.Requested, QuoteState.Rejected, QuoteActorKind.Customer },
        { QuoteState.Drafted, QuoteState.Rejected, QuoteActorKind.Customer },
        { QuoteState.Revised, QuoteState.Rejected, QuoteActorKind.Customer },
        { QuoteState.PendingApprover, QuoteState.Rejected, QuoteActorKind.Buyer },
        { QuoteState.Requested, QuoteState.Withdrawn, QuoteActorKind.Customer },
        { QuoteState.Revised, QuoteState.Withdrawn, QuoteActorKind.Buyer },
        // System voids on account-lifecycle.
        { QuoteState.Revised, QuoteState.Withdrawn, QuoteActorKind.System },
        // Worker expiry from any non-terminal.
        { QuoteState.Revised, QuoteState.Expired, QuoteActorKind.System },
        { QuoteState.PendingApprover, QuoteState.Expired, QuoteActorKind.System },
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void Allowed_transitions_return_true(QuoteState from, QuoteState to, QuoteActorKind actor)
    {
        QuoteStateMachine.CanTransition(from, to, actor).Should().BeTrue(
            $"{from} → {to} by {actor} is allowed per data-model §3.1");
    }

    public static TheoryData<QuoteState, QuoteState, QuoteActorKind> ForbiddenTransitions => new()
    {
        // Explicitly forbidden by data-model §3.1 "Forbidden":
        { QuoteState.Requested, QuoteState.Accepted, QuoteActorKind.Buyer },
        { QuoteState.Requested, QuoteState.Accepted, QuoteActorKind.AdminOperator },
        { QuoteState.Drafted, QuoteState.PendingApprover, QuoteActorKind.Buyer },
        { QuoteState.Drafted, QuoteState.Accepted, QuoteActorKind.AdminOperator },

        // Wrong actor for an otherwise-allowed move.
        { QuoteState.Requested, QuoteState.Drafted, QuoteActorKind.Customer },
        { QuoteState.Drafted, QuoteState.Revised, QuoteActorKind.Customer },
        { QuoteState.Revised, QuoteState.Accepted, QuoteActorKind.AdminOperator },
        { QuoteState.PendingApprover, QuoteState.Accepted, QuoteActorKind.Buyer },

        // Self-transitions forbidden — audit ledger requires real state changes.
        { QuoteState.Revised, QuoteState.Revised, QuoteActorKind.Buyer },
        { QuoteState.PendingApprover, QuoteState.PendingApprover, QuoteActorKind.Approver },

        // Terminal → non-terminal forbidden — reopening is a new quote.
        { QuoteState.Accepted, QuoteState.Revised, QuoteActorKind.Customer },
        { QuoteState.Rejected, QuoteState.Drafted, QuoteActorKind.AdminOperator },
        { QuoteState.Expired, QuoteState.Revised, QuoteActorKind.System },
        { QuoteState.Withdrawn, QuoteState.Drafted, QuoteActorKind.AdminOperator },

        // Terminal → terminal also forbidden (no double-decision).
        { QuoteState.Accepted, QuoteState.Rejected, QuoteActorKind.Customer },
        { QuoteState.Rejected, QuoteState.Withdrawn, QuoteActorKind.Buyer },
    };

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public void Forbidden_transitions_return_false(QuoteState from, QuoteState to, QuoteActorKind actor)
    {
        QuoteStateMachine.CanTransition(from, to, actor).Should().BeFalse(
            $"{from} → {to} by {actor} is forbidden per data-model §3.1");
    }

    [Fact]
    public void Token_round_trip_covers_every_state()
    {
        foreach (QuoteState s in Enum.GetValues<QuoteState>())
        {
            var token = s.ToToken();
            QuoteStateExtensions.ParseToken(token).Should().Be(s, $"round-trip for {s}");
        }
    }

    [Fact]
    public void Terminal_classification_matches_data_model_table()
    {
        QuoteState.Accepted.IsTerminal().Should().BeTrue();
        QuoteState.Rejected.IsTerminal().Should().BeTrue();
        QuoteState.Expired.IsTerminal().Should().BeTrue();
        QuoteState.Withdrawn.IsTerminal().Should().BeTrue();

        QuoteState.Requested.IsTerminal().Should().BeFalse();
        QuoteState.Drafted.IsTerminal().Should().BeFalse();
        QuoteState.Revised.IsTerminal().Should().BeFalse();
        QuoteState.PendingApprover.IsTerminal().Should().BeFalse();
    }
}
