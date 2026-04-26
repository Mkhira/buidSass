using BackendApi.Modules.Returns.Primitives;
using FluentAssertions;

namespace Returns.Tests.Unit;

public class RefundStateMachineTests
{
    [Theory]
    [InlineData(RefundStateMachine.Pending, RefundStateMachine.InProgress, true)]
    [InlineData(RefundStateMachine.Pending, RefundStateMachine.PendingManualTransfer, true)]
    [InlineData(RefundStateMachine.InProgress, RefundStateMachine.Completed, true)]
    [InlineData(RefundStateMachine.InProgress, RefundStateMachine.Failed, true)]
    [InlineData(RefundStateMachine.PendingManualTransfer, RefundStateMachine.Completed, true)]
    [InlineData(RefundStateMachine.Failed, RefundStateMachine.InProgress, true)]
    [InlineData(RefundStateMachine.Completed, RefundStateMachine.InProgress, false)]
    [InlineData(RefundStateMachine.Completed, RefundStateMachine.Failed, false)]
    [InlineData(RefundStateMachine.Pending, RefundStateMachine.Completed, false)]
    public void Refund_state_transitions(string from, string to, bool expected)
    {
        RefundStateMachine.IsValidTransition(from, to).Should().Be(expected);
    }
}

public class InspectionStateMachineTests
{
    [Theory]
    [InlineData(InspectionStateMachine.Pending, InspectionStateMachine.InProgress, true)]
    [InlineData(InspectionStateMachine.InProgress, InspectionStateMachine.Complete, true)]
    [InlineData(InspectionStateMachine.Complete, InspectionStateMachine.InProgress, false)]
    [InlineData(InspectionStateMachine.Pending, InspectionStateMachine.Complete, false)]
    public void Inspection_state_transitions(string from, string to, bool expected)
    {
        InspectionStateMachine.IsValidTransition(from, to).Should().Be(expected);
    }
}
