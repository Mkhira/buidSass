using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using MediatR;

namespace BackendApi.Modules.Payments.Features.WebhookReplay;

/// <summary>T046 — operator-initiated webhook replay for a provider + time window.</summary>
public sealed record WebhookReplayCommand(string ProviderId, DateTimeOffset From, DateTimeOffset To, Guid OperatorId)
    : IRequest<WebhookReplayResponse>;

public sealed record WebhookReplayResponse(bool Supported, int RepliedEventsCount, string? NotSupportedReason);

public sealed class WebhookReplayHandler : IRequestHandler<WebhookReplayCommand, WebhookReplayResponse>
{
    private readonly ProviderRegistry _providers;
    private readonly IAuditEventPublisher _audit;

    public WebhookReplayHandler(ProviderRegistry providers, IAuditEventPublisher audit)
    {
        _providers = providers;
        _audit = audit;
    }

    public async Task<WebhookReplayResponse> Handle(WebhookReplayCommand cmd, CancellationToken ct)
    {
        if (!_providers.TryResolve(cmd.ProviderId, out var provider) || provider is null)
        {
            throw new InvalidOperationException($"Unknown provider '{cmd.ProviderId}'");
        }
        if (cmd.From > cmd.To)
        {
            throw new InvalidOperationException("From must be <= To");
        }
        var result = await provider.ReplayWebhooksAsync(new DateRange(cmd.From, cmd.To), ct);
        await _audit.PublishAsync(new AuditEvent(
            ActorId: cmd.OperatorId, ActorRole: "payments-operator",
            Action: PaymentsConstants.AuditActions.PaymentWebhookReplay,
            EntityType: "WebhookReplay", EntityId: Guid.Empty,
            BeforeState: null,
            AfterState: new { provider = cmd.ProviderId, from = cmd.From, to = cmd.To, supported = result.Supported, count = result.RepliedEventsCount },
            Reason: result.NotSupportedReason), ct);
        return new WebhookReplayResponse(result.Supported, result.RepliedEventsCount, result.NotSupportedReason);
    }
}
