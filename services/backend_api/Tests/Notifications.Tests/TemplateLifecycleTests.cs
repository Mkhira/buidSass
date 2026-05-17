using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Privacy;
using BackendApi.Modules.Notifications.Subscribers;
using FluentAssertions;

namespace BackendApi.Tests.Notifications;

/// <summary>
/// T020 — unit coverage of the load-bearing invariants that protect
/// AC-4..AC-7 (template lifecycle), AC-26 (idempotency), and AC-27 (PII
/// redaction). Handler-level integration tests (full V-1 gate + V-4
/// transactional-disable rejection) layer on top of this in the Phase 7+ UAT
/// suite; the state-machine / redactor / idempotency-key surfaces tested
/// here are the actual correctness contracts those handlers enforce.
/// </summary>
public sealed class TemplateLifecycleTests
{
    [Theory]
    [InlineData("draft", "in_review", true)]
    [InlineData("in_review", "published", true)]
    [InlineData("in_review", "draft", true)]
    [InlineData("published", "archived", true)]
    [InlineData("draft", "archived", true)] // cleanup of stale drafts is allowed
    [InlineData("archived", "published", true)] // operator un-archive recovery path
    [InlineData("draft", "published", false)]
    [InlineData("published", "draft", false)]
    [InlineData("archived", "draft", false)]
    public void TemplateVersionStateMachine_AllowsCorrectTransitions(string from, string to, bool allowed)
    {
        TemplateVersionStateMachine.CanTransition(from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData("pending", "queued", true)]
    [InlineData("pending", "skipped", true)]
    [InlineData("queued", "sending", true)]
    [InlineData("sending", "delivered", true)]
    [InlineData("sending", "failed", true)]
    [InlineData("sending", "retrying", true)]
    [InlineData("retrying", "sending", true)]
    [InlineData("retrying", "dead_letter", true)]
    [InlineData("delivered", "sending", false)]
    [InlineData("dead_letter", "delivered", false)]
    public void NotificationStateMachine_AllowsCorrectTransitions(string from, string to, bool allowed)
    {
        NotificationStateMachine.CanTransition(from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData("draft", "scheduled", true)]
    [InlineData("scheduled", "sending", true)]
    [InlineData("sending", "completed", true)]
    [InlineData("sending", "paused", true)]
    [InlineData("paused", "sending", true)]
    [InlineData("scheduled", "cancelled", true)]
    [InlineData("completed", "sending", false)]
    [InlineData("cancelled", "scheduled", false)]
    public void CampaignStateMachine_AllowsCorrectTransitions(string from, string to, bool allowed)
    {
        CampaignStateMachine.CanTransition(from, to).Should().Be(allowed);
    }

    [Fact]
    public void NotificationStateMachine_EnsureTransition_ThrowsOnInvalid()
    {
        var act = () => NotificationStateMachine.EnsureTransition(
            NotificationsConstants.NotificationStates.Delivered,
            NotificationsConstants.NotificationStates.Sending);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IdempotencyKey_IsDeterministicAndChannelSensitive()
    {
        var correlation = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var key1 = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "order.placed", "email", recipient);
        var key2 = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "order.placed", "email", recipient);
        var key3 = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "order.placed", "sms", recipient);

        key1.Should().Be(key2, "same inputs must produce the same key (BR-3)");
        key1.Should().NotBe(key3, "different channel must produce a different key");
        key1.Should().HaveLength(64).And.MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void IdempotencyKey_IsEventKindSensitive()
    {
        // Two distinct events sharing the same correlation_id / channel /
        // recipient (e.g. order.placed and order.confirmed both keyed by
        // OrderId) MUST produce different idempotency keys — otherwise the
        // second event would collapse into the first as a duplicate.
        var correlation = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var placed = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "order.placed", "email", recipient);
        var confirmed = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "order.confirmed", "email", recipient);

        placed.Should().NotBe(confirmed);
    }

    [Fact]
    public void IdempotencyKey_AnonymousRecipientStillStable()
    {
        var correlation = Guid.NewGuid();
        var keyA = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "auth.otp_requested", "email", null);
        var keyB = NotificationEnqueuer.ComputeIdempotencyKey(correlation, "auth.otp_requested", "email", null);
        keyA.Should().Be(keyB);
    }

    [Theory]
    [InlineData("+966501234567", "+966****4567")]
    [InlineData("+201012345678", "+201****5678")] // \d{1,3} is greedy — captures 3-digit prefix
    public void PiiRedactor_MasksE164Phones(string input, string expected)
    {
        PiiRedactor.Redact(input).Should().Be(expected);
    }

    [Fact]
    public void PiiRedactor_StripsSaudiNationalId()
    {
        PiiRedactor.Redact("national_id 1234567890 belongs to user")
            .Should().Contain("[redacted-id]")
            .And.NotContain("1234567890");
    }

    [Fact]
    public void PiiRedactor_StripsPanShapedStrings()
    {
        PiiRedactor.Redact("card 4111111111111111 ending in 1111")
            .Should().Contain("[redacted-pan]")
            .And.NotContain("4111111111111111");
    }

    [Fact]
    public void PiiRedactor_MaskPhoneToLast4()
    {
        PiiRedactor.MaskPhoneToLast4("+966501234567").Should().Be("****4567");
        PiiRedactor.MaskPhoneToLast4("123").Should().Be("****");
    }
}
