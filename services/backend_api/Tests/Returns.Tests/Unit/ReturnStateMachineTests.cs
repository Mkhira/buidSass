using BackendApi.Modules.Returns.Primitives;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class ReturnStateMachineTests
{
    [Theory]
    [InlineData(ReturnStateMachine.PendingReview, ReturnStateMachine.Approved, true)]
    [InlineData(ReturnStateMachine.PendingReview, ReturnStateMachine.ApprovedPartial, true)]
    [InlineData(ReturnStateMachine.PendingReview, ReturnStateMachine.Rejected, true)]
    [InlineData(ReturnStateMachine.PendingReview, ReturnStateMachine.Refunded, true)]
    [InlineData(ReturnStateMachine.Approved, ReturnStateMachine.Received, true)]
    [InlineData(ReturnStateMachine.ApprovedPartial, ReturnStateMachine.Received, true)]
    [InlineData(ReturnStateMachine.Received, ReturnStateMachine.Inspected, true)]
    [InlineData(ReturnStateMachine.Inspected, ReturnStateMachine.Refunded, true)]
    [InlineData(ReturnStateMachine.Inspected, ReturnStateMachine.RefundFailed, true)]
    [InlineData(ReturnStateMachine.RefundFailed, ReturnStateMachine.Refunded, true)]
    public void Allowed_transitions_pass(string from, string to, bool expected)
    {
        ReturnStateMachine.IsValidTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(ReturnStateMachine.Refunded, ReturnStateMachine.Approved)]
    [InlineData(ReturnStateMachine.Rejected, ReturnStateMachine.Approved)]
    [InlineData(ReturnStateMachine.Approved, ReturnStateMachine.Inspected)]
    [InlineData(ReturnStateMachine.Approved, ReturnStateMachine.Refunded)]
    [InlineData(ReturnStateMachine.Received, ReturnStateMachine.Refunded)]
    public void Disallowed_transitions_blocked(string from, string to)
    {
        ReturnStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void Self_transition_idempotent()
    {
        foreach (var s in ReturnStateMachine.All)
        {
            ReturnStateMachine.IsValidTransition(s, s).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("unknown", "unknown")]
    [InlineData("", "")]
    [InlineData("not_a_state", "not_a_state")]
    public void Unknown_self_transitions_rejected(string from, string to)
    {
        // CR Major regression: the All.Contains guard before the switch must reject
        // garbage values even when from == to.
        ReturnStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void Case_insensitive_transitions_normalize_at_boundary()
    {
        ReturnStateMachine.IsValidTransition("PENDING_REVIEW", "APPROVED").Should().BeTrue();
        ReturnStateMachine.IsValidTransition("inspected", "REFUNDED").Should().BeTrue();
    }

    [Fact]
    public void SC_004_fuzz_no_illegal_accepted()
    {
        // 10k random pairs across all states → 0 accepted that shouldn't be.
        var rng = new Random(20260422);
        var states = ReturnStateMachine.All.ToArray();
        int illegalAccepted = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var from = states[rng.Next(states.Length)];
            var to = states[rng.Next(states.Length)];
            if (ReturnStateMachine.IsValidTransition(from, to)
                && !KnownAllowedPair(from, to)
                && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                illegalAccepted++;
            }
        }
        illegalAccepted.Should().Be(0);
    }

    private static bool KnownAllowedPair(string from, string to) =>
        (from, to) switch
        {
            (ReturnStateMachine.PendingReview, ReturnStateMachine.Approved) => true,
            (ReturnStateMachine.PendingReview, ReturnStateMachine.ApprovedPartial) => true,
            (ReturnStateMachine.PendingReview, ReturnStateMachine.Rejected) => true,
            (ReturnStateMachine.PendingReview, ReturnStateMachine.Refunded) => true,
            (ReturnStateMachine.Approved, ReturnStateMachine.Received) => true,
            (ReturnStateMachine.ApprovedPartial, ReturnStateMachine.Received) => true,
            (ReturnStateMachine.Received, ReturnStateMachine.Inspected) => true,
            (ReturnStateMachine.Inspected, ReturnStateMachine.Refunded) => true,
            (ReturnStateMachine.Inspected, ReturnStateMachine.RefundFailed) => true,
            (ReturnStateMachine.RefundFailed, ReturnStateMachine.Refunded) => true,
            _ => false,
        };
}
