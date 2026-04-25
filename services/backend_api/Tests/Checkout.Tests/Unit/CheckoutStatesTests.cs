using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Primitives;
using FluentAssertions;

namespace Checkout.Tests.Unit;

public sealed class CheckoutStatesTests
{
    [Theory]
    [InlineData(CheckoutStates.Init, CheckoutStates.Addressed, true)]
    [InlineData(CheckoutStates.Addressed, CheckoutStates.ShippingSelected, true)]
    [InlineData(CheckoutStates.ShippingSelected, CheckoutStates.PaymentSelected, true)]
    [InlineData(CheckoutStates.PaymentSelected, CheckoutStates.Submitted, true)]
    [InlineData(CheckoutStates.Submitted, CheckoutStates.Confirmed, true)]
    [InlineData(CheckoutStates.Submitted, CheckoutStates.Failed, true)]
    [InlineData(CheckoutStates.Failed, CheckoutStates.PaymentSelected, true)]
    [InlineData(CheckoutStates.PaymentSelected, CheckoutStates.Expired, true)]
    [InlineData(CheckoutStates.ShippingSelected, CheckoutStates.Addressed, true)]   // address change
    [InlineData(CheckoutStates.PaymentSelected, CheckoutStates.Addressed, true)]    // address change
    public void IsValidTransition_ReturnsTrueForAllowed(string from, string to, bool expected)
    {
        CheckoutStates.IsValidTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(CheckoutStates.Init, CheckoutStates.PaymentSelected)]     // skip steps
    [InlineData(CheckoutStates.Confirmed, CheckoutStates.Failed)]         // terminal
    [InlineData(CheckoutStates.Expired, CheckoutStates.Init)]             // terminal
    [InlineData(CheckoutStates.Submitted, CheckoutStates.Expired)]        // no expire during submit
    [InlineData(CheckoutStates.Confirmed, CheckoutStates.PaymentSelected)] // no rollback post-confirm
    public void IsValidTransition_RejectsIllegal(string from, string to)
    {
        CheckoutStates.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void TryTransition_Confirmed_StampsConfirmedAtOnce()
    {
        var session = new CheckoutSession { State = CheckoutStates.Submitted };
        var at1 = DateTimeOffset.UtcNow;
        var at2 = at1.AddMinutes(5);

        CheckoutStates.TryTransition(session, CheckoutStates.Confirmed, at1).Should().BeTrue();
        session.ConfirmedAt.Should().Be(at1);

        // Subsequent TryTransition to the same state is illegal (not in the transition table)
        // so ConfirmedAt should remain the first stamp, even if forced.
        CheckoutStates.TryTransition(session, CheckoutStates.Confirmed, at2).Should().BeFalse();
        session.ConfirmedAt.Should().Be(at1);
    }

    [Fact]
    public void TryTransition_Expired_CapturesTimestamp()
    {
        var session = new CheckoutSession { State = CheckoutStates.PaymentSelected };
        var at = DateTimeOffset.UtcNow;

        CheckoutStates.TryTransition(session, CheckoutStates.Expired, at).Should().BeTrue();
        session.State.Should().Be(CheckoutStates.Expired);
        session.ExpiredAt.Should().Be(at);
        session.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void TryTransition_InvalidFrom_LeavesSessionUntouched()
    {
        var session = new CheckoutSession
        {
            State = CheckoutStates.Confirmed,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        var originalUpdate = session.UpdatedAt;

        CheckoutStates.TryTransition(session, CheckoutStates.Failed, DateTimeOffset.UtcNow).Should().BeFalse();
        session.State.Should().Be(CheckoutStates.Confirmed);
        session.UpdatedAt.Should().Be(originalUpdate);
    }
}
