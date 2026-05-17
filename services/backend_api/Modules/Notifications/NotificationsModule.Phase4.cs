using BackendApi.Modules.Notifications.Features.Campaigns;
using BackendApi.Modules.Notifications.Features.Preferences;
using BackendApi.Modules.Notifications.UnsubscribeTokens;
using BackendApi.Modules.Notifications.Workers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// Phase 4 — campaigns + preferences + opt-out wiring (T036, T037, T038,
/// T039, T041). Two hosted services join the existing dispatch workers:
/// <c>CampaignScheduler</c> (30-second tick) and <c>SendingStuckReconciler</c>
/// (30-minute tick). The unsubscribe-token service is scoped so each request
/// gets a fresh signing context from configuration.
/// </summary>
public static partial class NotificationsModule
{
    internal static IServiceCollection AddPhase4Services(IServiceCollection services)
    {
        services.AddScoped<UnsubscribeTokenService>();
        services.AddHostedService<CampaignScheduler>();
        services.AddHostedService<SendingStuckReconciler>();
        return services;
    }

    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer)
    {
        MapPhase4CustomerEndpoints(customer);
    }

    internal static void MapPhase4AdminEndpoints(IEndpointRouteBuilder admin)
    {
        var campaigns = admin.MapGroup("/campaigns");
        campaigns.MapPost("/", async (HttpContext ctx, CreateCampaignRequest body, IMediator m, CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(ctx);
            if (actor is null) return Results.Unauthorized();
            var cmd = new CreateCampaignCommand(
                body.Name, body.TemplateId, body.TemplateVersionId, body.Channel,
                body.MarketCode, body.TargetCriteriaJson, body.SendAt,
                CreatedBy: actor.Value);
            return Results.Ok(new { id = await m.Send(cmd, ct) });
        });
        campaigns.MapPost("/{id:guid}:schedule", async (Guid id, ScheduleCampaignRequest body, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new ScheduleCampaignCommand(id, body.SendAt), ct);
            return Results.NoContent();
        });
        campaigns.MapPost("/{id:guid}:pause", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new PauseCampaignCommand(id), ct);
            return Results.NoContent();
        });
        campaigns.MapPost("/{id:guid}:resume", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new ResumeCampaignCommand(id), ct);
            return Results.NoContent();
        });
        campaigns.MapPost("/{id:guid}:cancel", async (Guid id, CancelCampaignRequest body, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new CancelCampaignCommand(id, body.Reason), ct);
            return Results.NoContent();
        });
        campaigns.MapGet("/{id:guid}/report", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var report = await m.Send(new GetCampaignReportQuery(id), ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });
    }

    internal static void MapPhase4CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        var prefs = customer.MapGroup("/preferences");
        // Self-service preferences read/write — customerId is resolved from the
        // authenticated principal, not the URL path, so a customer cannot read
        // or mutate another customer's preferences.
        prefs.MapGet("/me", async (HttpContext ctx, IMediator m, CancellationToken ct) =>
        {
            var customerId = ResolveAuthenticatedActorId(ctx);
            if (customerId is null) return Results.Unauthorized();
            return Results.Ok(await m.Send(new GetPreferencesQuery(customerId.Value), ct));
        });
        prefs.MapPut("/me", async (HttpContext ctx, UpdatePreferenceRequest body, IMediator m, CancellationToken ct) =>
        {
            var customerId = ResolveAuthenticatedActorId(ctx);
            if (customerId is null) return Results.Unauthorized();
            await m.Send(new UpdatePreferenceCommand(customerId.Value, body.Channel, body.Category, body.Enabled), ct);
            return Results.NoContent();
        });

        customer.MapPost("/unsubscribe", async (UnsubscribeRequest body, IMediator m, CancellationToken ct) =>
        {
            // Unsubscribe relies on a signed HMAC token carried in the link;
            // the token itself proves the customer's intent so no auth principal
            // is required (AC-21 footer-click works from email clients).
            var ok = await m.Send(new UnsubscribeCommand(body.Token), ct);
            return ok ? Results.Ok(new { unsubscribed = true }) : Results.BadRequest(new { unsubscribed = false, reason = "token_invalid_or_expired" });
        });
    }
}

// Server-derived: CreatedBy is intentionally absent — resolved from the
// authenticated principal so callers cannot forge audit attribution.
public sealed record CreateCampaignRequest(
    string Name,
    Guid TemplateId,
    Guid? TemplateVersionId,
    string Channel,
    string MarketCode,
    string TargetCriteriaJson,
    DateTimeOffset? SendAt);
public sealed record ScheduleCampaignRequest(DateTimeOffset SendAt);
public sealed record CancelCampaignRequest(string Reason);
public sealed record UpdatePreferenceRequest(string Channel, string Category, bool Enabled);
public sealed record UnsubscribeRequest(string Token);
