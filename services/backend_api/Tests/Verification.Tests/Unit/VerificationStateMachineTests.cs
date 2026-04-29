using BackendApi.Modules.Verification.Primitives;
using FluentAssertions;

namespace Verification.Tests.Unit;

/// <summary>
/// Property-style coverage of <see cref="VerificationStateMachine.CanTransition"/>
/// per spec 020 data-model §3.2. Exercises every allowed edge plus the three
/// forbidden-edge invariants: terminal→non-terminal, any→submitted,
/// info-requested→approved/rejected/revoked direct.
/// </summary>
public sealed class VerificationStateMachineTests
{
    public static TheoryData<VerificationState, VerificationState, VerificationActorKind> AllowedEdges()
    {
        var data = new TheoryData<VerificationState, VerificationState, VerificationActorKind>
        {
            // Reviewer pickup
            { VerificationState.Submitted, VerificationState.InReview, VerificationActorKind.Reviewer },

            // Reviewer decisions from in-review
            { VerificationState.InReview, VerificationState.Approved, VerificationActorKind.Reviewer },
            { VerificationState.InReview, VerificationState.Rejected, VerificationActorKind.Reviewer },
            { VerificationState.InReview, VerificationState.InfoRequested, VerificationActorKind.Reviewer },

            // Reviewer skip-explicit-begin
            { VerificationState.Submitted, VerificationState.Approved, VerificationActorKind.Reviewer },
            { VerificationState.Submitted, VerificationState.Rejected, VerificationActorKind.Reviewer },
            { VerificationState.Submitted, VerificationState.InfoRequested, VerificationActorKind.Reviewer },

            // Customer resubmits info-requested → in-review
            { VerificationState.InfoRequested, VerificationState.InReview, VerificationActorKind.Customer },

            // System: expiry / supersession
            { VerificationState.Approved, VerificationState.Expired, VerificationActorKind.System },
            { VerificationState.Approved, VerificationState.Superseded, VerificationActorKind.System },

            // Reviewer: revoke
            { VerificationState.Approved, VerificationState.Revoked, VerificationActorKind.Reviewer },
        };

        // System: void from any non-terminal
        foreach (var from in new[]
                 {
                     VerificationState.Submitted,
                     VerificationState.InReview,
                     VerificationState.InfoRequested,
                     VerificationState.Approved,
                 })
        {
            data.Add(from, VerificationState.Void, VerificationActorKind.System);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedEdges))]
    public void Allowed_edges_return_true(VerificationState from, VerificationState to, VerificationActorKind actor)
    {
        VerificationStateMachine.CanTransition(from, to, actor).Should().BeTrue(
            $"({from.ToWireValue()} → {to.ToWireValue()} as {actor.ToWireValue()}) is in the allowed transition table");
    }

    [Theory]
    [InlineData(VerificationState.Rejected)]
    [InlineData(VerificationState.Expired)]
    [InlineData(VerificationState.Revoked)]
    [InlineData(VerificationState.Superseded)]
    [InlineData(VerificationState.Void)]
    public void Terminal_states_block_every_outgoing_transition(VerificationState terminal)
    {
        terminal.IsTerminal().Should().BeTrue();

        foreach (var to in Enum.GetValues<VerificationState>())
        {
            foreach (var actor in Enum.GetValues<VerificationActorKind>())
            {
                VerificationStateMachine.CanTransition(terminal, to, actor).Should().BeFalse(
                    $"terminal source '{terminal}' MUST never transition (attempted '{to}' as '{actor}')");
            }
        }
    }

    [Fact]
    public void No_state_can_transition_back_to_submitted()
    {
        foreach (var from in Enum.GetValues<VerificationState>())
        {
            foreach (var actor in Enum.GetValues<VerificationActorKind>())
            {
                VerificationStateMachine.CanTransition(from, VerificationState.Submitted, actor)
                    .Should().BeFalse($"resubmission requires a NEW row, not a transition (from={from}, actor={actor})");
            }
        }
    }

    [Theory]
    [InlineData(VerificationState.Approved, VerificationActorKind.Reviewer)]
    [InlineData(VerificationState.Rejected, VerificationActorKind.Reviewer)]
    [InlineData(VerificationState.Revoked, VerificationActorKind.Reviewer)]
    public void Info_requested_cannot_skip_in_review(VerificationState skipTarget, VerificationActorKind actor)
    {
        VerificationStateMachine.CanTransition(VerificationState.InfoRequested, skipTarget, actor)
            .Should().BeFalse($"info-requested → {skipTarget} direct is forbidden — must round-trip via in-review");
    }

    [Fact]
    public void Customer_cannot_perform_reviewer_or_system_only_transitions()
    {
        // Customer cannot approve / reject / revoke / expire / supersede / void.
        var reviewerOrSystemOnly = new[]
        {
            VerificationState.Approved,
            VerificationState.Rejected,
            VerificationState.Revoked,
            VerificationState.Expired,
            VerificationState.Superseded,
            VerificationState.Void,
        };

        foreach (var from in Enum.GetValues<VerificationState>())
        {
            foreach (var to in reviewerOrSystemOnly)
            {
                VerificationStateMachine.CanTransition(from, to, VerificationActorKind.Customer)
                    .Should().BeFalse(
                        $"customers cannot drive '{from} → {to}' transitions (reviewer or system only)");
            }
        }
    }

    [Fact]
    public void EnsureCanTransitionOrThrow_throws_with_diagnostic_payload_on_blocked_edge()
    {
        var act = () => VerificationStateMachine.EnsureCanTransitionOrThrow(
            VerificationState.Approved,
            VerificationState.Submitted,
            VerificationActorKind.Customer);

        act.Should().Throw<InvalidVerificationTransitionException>()
            .Where(e => e.From == VerificationState.Approved
                     && e.To == VerificationState.Submitted
                     && e.Actor == VerificationActorKind.Customer);
    }

    [Fact]
    public void EnsureCanTransitionOrThrow_is_a_noop_on_allowed_edge()
    {
        var act = () => VerificationStateMachine.EnsureCanTransitionOrThrow(
            VerificationState.Submitted,
            VerificationState.InReview,
            VerificationActorKind.Reviewer);

        act.Should().NotThrow();
    }
}
