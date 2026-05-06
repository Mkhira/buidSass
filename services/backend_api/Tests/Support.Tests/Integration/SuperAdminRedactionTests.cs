using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using BackendApi.Modules.Support.SuperAdmin.RedactAttachment;
using BackendApi.Modules.Support.SuperAdmin.RedactMessage;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Support.Tests.Infrastructure;

namespace Support.Tests.Integration;

/// <summary>
/// Spec 023 Phase 10 — covers FR-012a (super-admin attachment redaction) and
/// FR-011a (super-admin message redaction). Both flows must be idempotent on
/// re-attempt with stable reason codes.
/// </summary>
[Collection(nameof(SupportPostgresCollection))]
public sealed class SuperAdminRedactionTests
{
    private readonly SupportPostgresFixture _fx;
    public SuperAdminRedactionTests(SupportPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task RedactAttachment_Wipes_StorageObjectId_And_Stamps_State()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T18:00:00+00:00");
        var (ticketId, messageId, attachmentId) = await SeedAsync(ctx, nowUtc);

        var publisher = new RecordingPublisher();
        var handler = new RedactAttachmentHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new RedactAttachmentCommand(
            ticketId, attachmentId, Guid.NewGuid(),
            "Customer requested redaction — exposes sensitive PII."), default);

        result.Success.Should().BeTrue();

        await using var verify = _fx.NewContext();
        var attachment = await verify.Attachments.AsNoTracking()
            .FirstAsync(a => a.Id == attachmentId);
        attachment.State.Should().Be(TicketAttachmentState.Redacted);
        attachment.StorageObjectId.Should().BeNull();
        attachment.RedactedAtUtc.Should().Be(nowUtc);
        attachment.RedactedByRole.Should().Be(TicketActorKindNames.SuperAdmin);
        attachment.RedactionReasonNote.Should().StartWith("Customer requested redaction");

        publisher.Notifications.OfType<TicketAttachmentRedacted>().Should().HaveCount(1);
    }

    [Fact]
    public async Task RedactAttachment_Is_Idempotent_On_Already_Redacted()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T18:30:00+00:00");
        var (ticketId, _, attachmentId) = await SeedAsync(ctx, nowUtc);

        var publisher = new RecordingPublisher();
        var handler = new RedactAttachmentHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var first = await handler.HandleAsync(new RedactAttachmentCommand(
            ticketId, attachmentId, Guid.NewGuid(),
            "Initial redaction reason."), default);
        first.Success.Should().BeTrue();

        // Re-attempt — handler should report already-redacted, not double-emit.
        var second = await handler.HandleAsync(new RedactAttachmentCommand(
            ticketId, attachmentId, Guid.NewGuid(),
            "Second attempt reason."), default);

        second.Success.Should().BeFalse();
        second.ReasonCode.Should().Be(TicketReasonCode.RedactionAttachmentAlreadyRedacted);
        publisher.Notifications.OfType<TicketAttachmentRedacted>().Should().HaveCount(1);
    }

    [Fact]
    public async Task RedactAttachment_Rejects_Short_Reason()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T18:45:00+00:00");
        var (ticketId, _, attachmentId) = await SeedAsync(ctx, nowUtc);

        var publisher = new RecordingPublisher();
        var handler = new RedactAttachmentHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new RedactAttachmentCommand(
            ticketId, attachmentId, Guid.NewGuid(), "short"), default);

        result.Success.Should().BeFalse();
        result.ReasonCode.Should().Be(TicketReasonCode.RedactionReasonRequired);
        publisher.Notifications.OfType<TicketAttachmentRedacted>().Should().BeEmpty();
    }

    [Fact]
    public async Task RedactMessage_Wipes_Body_And_Captures_Originating_Link()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T19:00:00+00:00");
        var (ticketId, messageId, _) = await SeedAsync(ctx, nowUtc, withCustomerReply: true);
        var requestingCustomerId = await ctx.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId).Select(t => t.CustomerId).FirstAsync();

        var publisher = new RecordingPublisher();
        var handler = new RedactMessageHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new RedactMessageCommand(
            ticketId, messageId, Guid.NewGuid(),
            "Customer-requested redaction of personal data.",
            OriginatingRedactionRequestTicketId: ticketId,
            RequestingCustomerId: requestingCustomerId), default);

        result.Success.Should().BeTrue();

        await using var verify = _fx.NewContext();
        var message = await verify.Messages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.Body.Should().BeNull();
        message.RedactedAtUtc.Should().Be(nowUtc);
        message.RedactedByRole.Should().Be(TicketActorKindNames.SuperAdmin);
        message.OriginatingRedactionRequestTicketId.Should().Be(ticketId);

        publisher.Notifications.OfType<TicketMessageRedacted>().Should().HaveCount(1);
    }

    [Fact]
    public async Task RedactMessage_Rejects_System_Event_Bodies()
    {
        await using var ctx = _fx.NewContext();
        await Truncate.AllAsync(ctx);

        var nowUtc = DateTimeOffset.Parse("2026-05-06T19:30:00+00:00");
        var (ticketId, messageId, _) = await SeedAsync(ctx, nowUtc, withSystemEvent: true);

        var publisher = new RecordingPublisher();
        var handler = new RedactMessageHandler(ctx, publisher, new FakeTimeProvider(nowUtc));

        var result = await handler.HandleAsync(new RedactMessageCommand(
            ticketId, messageId, Guid.NewGuid(),
            "Attempting to redact a system-event body.",
            OriginatingRedactionRequestTicketId: null,
            RequestingCustomerId: null), default);

        result.Success.Should().BeFalse();
        result.ReasonCode.Should().Be(TicketReasonCode.RedactionMessageNotRedactable);
    }

    private static async Task<(Guid ticketId, Guid messageId, Guid attachmentId)> SeedAsync(
        SupportDbContext ctx,
        DateTimeOffset nowUtc,
        bool withCustomerReply = false,
        bool withSystemEvent = false)
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        ctx.Tickets.Add(new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            MarketCode = "SA",
            Locale = "en",
            Category = "general_question",
            Priority = "normal",
            State = TicketStateNames.InProgress,
            Subject = "Redaction test",
            Body = "Body",
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = nowUtc.AddMinutes(240),
            ResolutionDueUtc = nowUtc.AddMinutes(2880),
            CreatedAtUtc = nowUtc.AddMinutes(-30),
            UpdatedAtUtc = nowUtc.AddMinutes(-30),
        });

        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        if (withSystemEvent)
        {
            ctx.Messages.Add(new TicketMessage
            {
                Id = messageId,
                TicketId = ticketId,
                Kind = TicketMessageKindNames.SystemEvent,
                ActorId = null,
                ActorRole = TicketActorKindNames.System,
                Body = null,
                BodyLocale = null,
                LeadIntervention = false,
                CreatedAtUtc = nowUtc.AddMinutes(-29),
            });
        }
        else
        {
            ctx.Messages.Add(new TicketMessage
            {
                Id = messageId,
                TicketId = ticketId,
                Kind = withCustomerReply ? TicketMessageKindNames.CustomerReply : TicketMessageKindNames.AgentReply,
                ActorId = withCustomerReply ? customerId : Guid.NewGuid(),
                ActorRole = withCustomerReply ? TicketActorKindNames.Customer : TicketActorKindNames.Agent,
                Body = "Original body containing PII.",
                BodyLocale = "en",
                LeadIntervention = false,
                CreatedAtUtc = nowUtc.AddMinutes(-29),
            });
        }

        ctx.Attachments.Add(new TicketAttachment
        {
            Id = attachmentId,
            MessageId = messageId,
            TicketId = ticketId,
            StorageObjectId = "blob/test/" + attachmentId,
            MimeType = "image/png",
            SizeBytes = 1024,
            OriginalFilename = "evidence.png",
            State = TicketAttachmentState.Active,
            CreatedAtUtc = nowUtc.AddMinutes(-29),
        });

        await ctx.SaveChangesAsync();
        return (ticketId, messageId, attachmentId);
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<INotification> Notifications { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is INotification n)
            {
                lock (Notifications) Notifications.Add(n);
            }
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            lock (Notifications) Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
