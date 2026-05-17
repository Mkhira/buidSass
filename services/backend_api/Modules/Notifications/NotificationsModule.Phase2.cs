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
            [FromBody] CreateDraftRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                var response = await mediator.Send(new CreateDraftCommand(
                    body.EventKind,
                    body.BodyAr,
                    body.BodyEn,
                    body.SubjectAr,
                    body.SubjectEn,
                    body.Placeholders ?? Array.Empty<string>(),
                    body.AuthorId), ct);
                return Results.Created($"/admin/notifications/templates/{response.TemplateId}", response);
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:submit — submit for review (T013).
        templates.MapPost("/{id:guid}:submit", async (
            Guid id,
            [FromBody] SubmitForReviewRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new SubmitForReviewCommand(id, body.ActorId), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:approve — V-1 publish gate (T014).
        templates.MapPost("/{id:guid}:approve", async (
            Guid id,
            [FromBody] ApproveRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new ApproveCommand(id, body.ReviewerId, body.ArEditorialReviewed), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:reject — back to draft (T015).
        templates.MapPost("/{id:guid}:reject", async (
            Guid id,
            [FromBody] RejectRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new RejectCommand(id, body.ReviewerId, body.Comment), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /admin/notifications/templates/{id}:archive (T016).
        templates.MapPost("/{id:guid}:archive", async (
            Guid id,
            [FromBody] ArchiveRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new ArchiveCommand(id, body.ActorId, body.Reason), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ValidationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

/// <summary>
/// Anchor type used by <c>RegisterServicesFromAssemblyContaining&lt;T&gt;</c>
/// to pin MediatR's assembly scan to the Notifications module.
/// </summary>
public sealed class NotificationsModuleAnchor { }

// === Endpoint request DTOs ===

public sealed record CreateDraftRequest(
    string EventKind,
    string BodyAr,
    string BodyEn,
    string? SubjectAr,
    string? SubjectEn,
    IReadOnlyList<string>? Placeholders,
    Guid AuthorId);

public sealed record SubmitForReviewRequest(Guid ActorId);
public sealed record ApproveRequest(Guid ReviewerId, bool ArEditorialReviewed);
public sealed record RejectRequest(Guid ReviewerId, string Comment);
public sealed record ArchiveRequest(Guid ActorId, string? Reason);
