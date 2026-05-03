using BackendApi.Modules.B2B.Primitives;
using FluentAssertions;

namespace B2B.Tests.Unit;

/// <summary>Spec 021 T016 — invitation lifecycle (data-model §3.2).</summary>
public sealed class CompanyInvitationStateMachineTests
{
    [Theory]
    [InlineData(CompanyInvitationState.Pending, CompanyInvitationState.Accepted)]
    [InlineData(CompanyInvitationState.Pending, CompanyInvitationState.Declined)]
    [InlineData(CompanyInvitationState.Pending, CompanyInvitationState.Expired)]
    public void Allowed_transitions_from_pending_return_true(CompanyInvitationState from, CompanyInvitationState to)
    {
        CompanyInvitationStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(CompanyInvitationState.Accepted, CompanyInvitationState.Pending)]
    [InlineData(CompanyInvitationState.Declined, CompanyInvitationState.Pending)]
    [InlineData(CompanyInvitationState.Expired, CompanyInvitationState.Pending)]
    [InlineData(CompanyInvitationState.Accepted, CompanyInvitationState.Declined)]
    [InlineData(CompanyInvitationState.Declined, CompanyInvitationState.Expired)]
    public void Terminal_transitions_return_false(CompanyInvitationState from, CompanyInvitationState to)
    {
        CompanyInvitationStateMachine.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void Pending_self_transition_is_forbidden()
    {
        CompanyInvitationStateMachine
            .CanTransition(CompanyInvitationState.Pending, CompanyInvitationState.Pending)
            .Should().BeFalse();
    }

    [Fact]
    public void Token_round_trip_covers_every_state()
    {
        foreach (CompanyInvitationState s in Enum.GetValues<CompanyInvitationState>())
        {
            var token = s.ToToken();
            CompanyInvitationStateExtensions.TryParseToken(token, out var parsed).Should().BeTrue();
            parsed.Should().Be(s);
        }
    }
}
