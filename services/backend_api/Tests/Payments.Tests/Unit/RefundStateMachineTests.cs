using BackendApi.Modules.Payments.Domain.StateMachines;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

public sealed class RefundStateMachineTests
{
    [Theory]
    [InlineData("pending", "completed", true)]
    [InlineData("pending", "failed", true)]
    [InlineData("completed", "failed", false)]
    [InlineData("completed", "pending", false)]
    [InlineData("failed", "completed", false)]
    public void Transitions_HonorContract(string from, string to, bool allowed)
    {
        RefundStateMachine.CanTransition(from, to).Should().Be(allowed);
        if (allowed)
        {
            Action act = () => RefundStateMachine.EnsureTransition(from, to);
            act.Should().NotThrow();
        }
        else
        {
            Action act = () => RefundStateMachine.EnsureTransition(from, to);
            act.Should().Throw<InvalidOperationException>();
        }
    }
}

public sealed class ReconciliationExceptionStateMachineTests
{
    [Fact]
    public void OpenToResolved_Allowed()
    {
        ReconciliationExceptionStateMachine.CanTransition("open", "resolved").Should().BeTrue();
    }

    [Fact]
    public void ResolvedTerminal()
    {
        ReconciliationExceptionStateMachine.CanTransition("resolved", "open").Should().BeFalse();
    }

    [Theory]
    [InlineData("refund_issued", true)]
    [InlineData("internal_correction", true)]
    [InlineData("provider_correction_requested", true)]
    [InlineData("accepted_loss", true)]
    [InlineData("not_a_valid_action", false)]
    [InlineData(null, false)]
    public void IsValidResolution_MatchesContract(string? resolution, bool expected)
    {
        ReconciliationExceptionStateMachine.IsValidResolution(resolution).Should().Be(expected);
    }
}
