using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Primitives;
using FluentAssertions;

namespace BackendApi.Tests.Payments.Unit;

/// <summary>T011 + AC-37 — exercises every legal transition in PaymentStateMachine.</summary>
public sealed class PaymentStateMachineTests
{
    [Theory]
    [InlineData("pending_authorization", "captured")]
    [InlineData("pending_authorization", "authorized")]
    [InlineData("pending_authorization", "failed")]
    [InlineData("pending_authorization", "expired")]
    [InlineData("authorized", "captured")]
    [InlineData("authorized", "capture_failed")]
    [InlineData("capture_failed", "captured")]
    [InlineData("capture_failed", "expired")]
    [InlineData("pending_external_redirect", "captured")]
    [InlineData("pending_external_redirect", "failed")]
    [InlineData("pending_external_redirect", "expired")]
    [InlineData("pending_collection_on_delivery", "captured")]
    [InlineData("pending_collection_on_delivery", "failed")]
    [InlineData("pending_bank_transfer", "captured")]
    [InlineData("pending_bank_transfer", "expired")]
    [InlineData("captured", "refunded")]
    [InlineData("captured", "partially_refunded")]
    [InlineData("captured", "chargeback_received")]
    [InlineData("partially_refunded", "refunded")]
    [InlineData("partially_refunded", "partially_refunded")]
    public void LegalTransition_Allowed(string from, string to)
    {
        PaymentStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData("failed", "captured")]
    [InlineData("expired", "captured")]
    [InlineData("captured", "pending_authorization")]
    [InlineData("refunded", "captured")]
    [InlineData("pending_authorization", "pending_bank_transfer")]
    [InlineData("chargeback_received", "captured")]
    public void IllegalTransition_Rejected(string from, string to)
    {
        PaymentStateMachine.CanTransition(from, to).Should().BeFalse();
        Action act = () => PaymentStateMachine.EnsureTransition(from, to);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("card", "pending_authorization")]
    [InlineData("mada", "pending_authorization")]
    [InlineData("apple_pay", "pending_authorization")]
    [InlineData("stc_pay", "pending_authorization")]
    [InlineData("meeza", "pending_authorization")]
    [InlineData("bnpl_tabby", "pending_external_redirect")]
    [InlineData("bnpl_tamara", "pending_external_redirect")]
    [InlineData("bnpl_valu", "pending_external_redirect")]
    [InlineData("cod", "pending_collection_on_delivery")]
    [InlineData("bank_transfer", "pending_bank_transfer")]
    public void InitialStateForMethod_MapsExpectedState(string method, string expected)
    {
        PaymentStateMachine.InitialStateForMethod(method).Should().Be(expected);
    }
}
