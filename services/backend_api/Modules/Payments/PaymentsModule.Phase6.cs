using BackendApi.Modules.Payments.Features.WebhookReplay;
using BackendApi.Modules.Payments.Webhooks;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 6 — webhook endpoints + replay.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase6Slices(IServiceCollection services)
    {
        services.AddScoped<PaymentsWebhookHandler>();
        services.AddScoped<WebhookReplayHandler>();
    }

    static partial void MapWebhookEndpointsPhase6(IEndpointRouteBuilder webhooks)
    {
        // POST /v1/payments/webhooks/{provider}
        webhooks.MapPost("/{provider}", async (
            string provider,
            HttpRequest request,
            [FromServices] PaymentsWebhookHandler handler,
            CancellationToken ct) => await handler.HandleAsync(provider, request, ct));
    }

    static partial void MapAdminEndpointsPhase6(IEndpointRouteBuilder admin)
    {
        // POST /v1/admin/payments/webhook-replay
        admin.MapPost("/webhook-replay", async (
            [FromBody] WebhookReplayRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new WebhookReplayCommand(body.Provider, body.From, body.To, body.OperatorId), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record WebhookReplayRequest(string Provider, DateTimeOffset From, DateTimeOffset To, Guid OperatorId);
