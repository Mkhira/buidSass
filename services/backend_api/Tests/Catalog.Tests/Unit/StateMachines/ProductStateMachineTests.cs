using BackendApi.Modules.Catalog.Primitives.StateMachines;
using FluentAssertions;

namespace Catalog.Tests.Unit.StateMachines;

public sealed class ProductStateMachineTests
{
    private readonly ProductStateMachine _stateMachine = new();

    [Theory]
    [InlineData(ProductState.Draft, ProductTrigger.Submit, ProductState.InReview)]
    [InlineData(ProductState.InReview, ProductTrigger.Withdraw, ProductState.Draft)]
    [InlineData(ProductState.InReview, ProductTrigger.Publish, ProductState.Published)]
    [InlineData(ProductState.InReview, ProductTrigger.PublishWithFutureAt, ProductState.Scheduled)]
    [InlineData(ProductState.Scheduled, ProductTrigger.WorkerFire, ProductState.Published)]
    [InlineData(ProductState.Scheduled, ProductTrigger.CancelSchedule, ProductState.InReview)]
    [InlineData(ProductState.Published, ProductTrigger.Archive, ProductState.Archived)]
    [InlineData(ProductState.Archived, ProductTrigger.Unarchive, ProductState.Draft)]
    public void TryTransition_ValidTransitions_ReturnsTrueAndNextState(ProductState from, ProductTrigger trigger, ProductState expected)
    {
        var ok = _stateMachine.TryTransition(from, trigger, out var next);

        ok.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(ProductState.Draft, ProductTrigger.Publish)]
    [InlineData(ProductState.Draft, ProductTrigger.Archive)]
    [InlineData(ProductState.Archived, ProductTrigger.Submit)]
    [InlineData(ProductState.Published, ProductTrigger.Submit)]
    [InlineData(ProductState.Scheduled, ProductTrigger.Submit)]
    public void TryTransition_InvalidTransitions_ReturnsFalseAndUnchangedState(ProductState from, ProductTrigger trigger)
    {
        var ok = _stateMachine.TryTransition(from, trigger, out var next);

        ok.Should().BeFalse();
        next.Should().Be(from);
    }

    [Theory]
    [InlineData("draft", ProductState.Draft)]
    [InlineData("in_review", ProductState.InReview)]
    [InlineData("scheduled", ProductState.Scheduled)]
    [InlineData("published", ProductState.Published)]
    [InlineData("archived", ProductState.Archived)]
    [InlineData("IN_REVIEW", ProductState.InReview)]
    public void TryParse_RecognizedValues_ReturnsTrue(string value, ProductState expected)
    {
        var ok = ProductStateMachine.TryParse(value, out var state);

        ok.Should().BeTrue();
        state.Should().Be(expected);
    }

    [Fact]
    public void TryParse_UnknownValue_ReturnsFalse()
    {
        var ok = ProductStateMachine.TryParse("bogus", out var state);

        ok.Should().BeFalse();
        state.Should().Be(ProductState.Draft);
    }

    [Theory]
    [InlineData(ProductState.Draft, "draft")]
    [InlineData(ProductState.InReview, "in_review")]
    [InlineData(ProductState.Scheduled, "scheduled")]
    [InlineData(ProductState.Published, "published")]
    [InlineData(ProductState.Archived, "archived")]
    public void Encode_KnownStates_ReturnsSpecCanonicalString(ProductState state, string expected)
    {
        ProductStateMachine.Encode(state).Should().Be(expected);
    }
}
