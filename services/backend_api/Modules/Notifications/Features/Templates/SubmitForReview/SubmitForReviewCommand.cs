using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Templates.SubmitForReview;

/// <summary>
/// T013 — transitions a <c>draft</c> <see cref="TemplateVersion"/> to
/// <c>in_review</c> and emits the <c>template.submitted_for_review</c>
/// audit event (US3 acceptance #2).
/// </summary>
public sealed record SubmitForReviewCommand(Guid TemplateVersionId, Guid ActorId) : IRequest;

public sealed class SubmitForReviewValidator : AbstractValidator<SubmitForReviewCommand>
{
    public SubmitForReviewValidator()
    {
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}

public sealed class SubmitForReviewHandler : IRequestHandler<SubmitForReviewCommand>
{
    private readonly NotificationsDbContext _db;
    private readonly IValidator<SubmitForReviewCommand> _validator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public SubmitForReviewHandler(
        NotificationsDbContext db,
        IValidator<SubmitForReviewCommand> validator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public async Task Handle(SubmitForReviewCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var version = await _db.TemplateVersions
            .FirstOrDefaultAsync(v => v.Id == command.TemplateVersionId && v.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"TemplateVersion {command.TemplateVersionId} not found.");

        TemplateVersionStateMachine.EnsureTransition(
            version.State, NotificationsConstants.TemplateVersionStates.InReview);

        version.State = NotificationsConstants.TemplateVersionStates.InReview;
        version.SubmittedAt = _clock.GetUtcNow();
        version.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: command.ActorId,
            ActorRole: "template-author",
            Action: "template.submitted_for_review",
            EntityType: "TemplateVersion",
            EntityId: version.Id,
            BeforeState: new { state = NotificationsConstants.TemplateVersionStates.Draft },
            AfterState: new
            {
                template_id = version.TemplateId,
                version_id = version.Id,
                author_id = version.AuthorId,
                state = version.State,
            },
            Reason: null), ct);
    }
}
