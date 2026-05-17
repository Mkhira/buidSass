using BackendApi.Modules.Notifications.Features.Templates.Approve;
using BackendApi.Modules.Notifications.Features.Templates.Archive;
using BackendApi.Modules.Notifications.Features.Templates.CreateDraft;
using BackendApi.Modules.Notifications.Features.Templates.Reject;
using BackendApi.Modules.Notifications.Features.Templates.SubmitForReview;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// Phase 2 slice — Template authoring lifecycle. Adds MediatR + Validators
/// for CreateDraft / SubmitForReview / Approve / Reject / Archive and maps
/// the five admin endpoints under <c>/admin/notifications/templates</c>.
/// </summary>
public static partial class NotificationsModule
{
    static partial void AddTemplating(IServiceCollection services)
    {
        // MediatR — idempotent across modules; appends our assembly's handlers.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<NotificationsModuleAnchor>());

        services.AddScoped<IValidator<CreateDraftCommand>, CreateDraftValidator>();
        services.AddScoped<IValidator<SubmitForReviewCommand>, SubmitForReviewValidator>();
        services.AddScoped<IValidator<ApproveCommand>, ApproveValidator>();
        services.AddScoped<IValidator<RejectCommand>, RejectValidator>();
        services.AddScoped<IValidator<ArchiveCommand>, ArchiveValidator>();
    }

    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin)
    {
        MapTemplateEndpoints(admin);
        MapPhase4AdminEndpoints(admin);
        MapPhase5AdminEndpoints(admin);
    }

    private static void MapTemplateEndpoints(IEndpointRouteBuilder admin)
    {
        var templates = admin.MapGroup("/templates");

        // POST /admin/notifications/templates — create a draft (T012).
        templates.MapPost("", async (
            HttpContext httpContext,
            [FromBody] CreateDraftRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(httpContext);
            if (actor is null) return Results.Unauthorized();
            try
            {
                var response = await mediator.Send(new CreateDraftCommand(
                    body.EventKind,
                    body.BodyAr,
                    body.BodyEn,
                    body.SubjectAr,
                    body.SubjectEn,
                    body.Placeholders ?? Array.Empty<string>(),
                    actor.Value), ct);
                return Results.Created($"/admin/notifications/templates/{response.TemplateId}", response);
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:submit — submit for review (T013).
        templates.MapPost("/{id:guid}:submit", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(httpContext);
            if (actor is null) return Results.Unauthorized();
            try
            {
                await mediator.Send(new SubmitForReviewCommand(id, actor.Value), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:approve — V-1 publish gate (T014).
        templates.MapPost("/{id:guid}:approve", async (
            Guid id,
            HttpContext httpContext,
            [FromBody] ApproveRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(httpContext);
            if (actor is null) return Results.Unauthorized();
            try
            {
                await mediator.Send(new ApproveCommand(id, actor.Value, body.ArEditorialReviewed), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:reject — back to draft (T015).
        templates.MapPost("/{id:guid}:reject", async (
            Guid id,
            HttpContext httpContext,
            [FromBody] RejectRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(httpContext);
            if (actor is null) return Results.Unauthorized();
            try
            {
                await mediator.Send(new RejectCommand(id, actor.Value, body.Comment), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:archive (T016).
        templates.MapPost("/{id:guid}:archive", async (
            Guid id,
            HttpContext httpContext,
            [FromBody] ArchiveRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(httpContext);
            if (actor is null) return Results.Unauthorized();
            try
            {
                await mediator.Send(new ArchiveCommand(id, actor.Value, body.Reason), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    /// <summary>
    /// Resolves the authenticated admin/operator id from the request principal
    /// (<c>sub</c> claim with <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>
    /// fallback). Matches the Payments / Identity pattern. Returns <c>null</c>
    /// when the request is unauthenticated so callers map to HTTP 401 instead of
    /// trusting a body-supplied id (a client could otherwise forge audit attribution).
    /// </summary>
    internal static Guid? ResolveAuthenticatedActorId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>
/// Anchor type used by <c>RegisterServicesFromAssemblyContaining&lt;T&gt;</c>
/// to pin MediatR's assembly scan to the Notifications module.
/// </summary>
public sealed class NotificationsModuleAnchor { }

// === Endpoint request DTOs ===

// Actor identity is intentionally absent from these DTOs — the server resolves
// it from the authenticated principal so a client cannot forge audit attribution
// (cross-account impersonation vector). Pattern matches Payments / Identity.
public sealed record CreateDraftRequest(
    string EventKind,
    string BodyAr,
    string BodyEn,
    string? SubjectAr,
    string? SubjectEn,
    IReadOnlyList<string>? Placeholders);

public sealed record ApproveRequest(bool ArEditorialReviewed);
public sealed record RejectRequest(string Comment);
public sealed record ArchiveRequest(string? Reason);
