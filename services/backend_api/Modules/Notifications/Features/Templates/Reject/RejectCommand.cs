using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Templates.Reject;

/// <summary>
/// T015 — rejects an <c>in_review</c> <see cref="TemplateVersion"/> back to
/// <c>draft</c> with a mandatory reviewer comment. Reviewer must differ
/// from author for the same single-author-publish reason as Approve (V-1).
/// </summary>
public sealed record RejectCommand(
    Guid TemplateVersionId,
    Guid ReviewerId,
    string Comment) : IRequest;

public sealed class RejectValidator : AbstractValidator<RejectCommand>
{
    public RejectValidator()
    {
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(4096);
    }
}

public sealed class RejectHandler : IRequestHandler<RejectCommand>
{
    private readonly NotificationsDbContext _db;
    private readonly IValidator<RejectCommand> _validator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public RejectHandler(
        NotificationsDbContext db,
        IValidator<RejectCommand> validator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public async Task Handle(RejectCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var version = await _db.TemplateVersions
            .FirstOrDefaultAsync(v => v.Id == command.TemplateVersionId && v.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"TemplateVersion {command.TemplateVersionId} not found.");

        if (version.AuthorId == command.ReviewerId)
        {
            throw new InvalidOperationException("V-1: reviewer_id must differ from author_id (reject).");
        }

        TemplateVersionStateMachine.EnsureTransition(
            version.State, NotificationsConstants.TemplateVersionStates.Draft);

        version.State = NotificationsConstants.TemplateVersionStates.Draft;
        version.ReviewerId = command.ReviewerId;
        version.ReviewerComment = command.Comment;
        version.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: command.ReviewerId,
            ActorRole: "template-reviewer",
            Action: "template.rejected",
            EntityType: "TemplateVersion",
            EntityId: version.Id,
            BeforeState: new { state = NotificationsConstants.TemplateVersionStates.InReview },
            AfterState: new
            {
                template_id = version.TemplateId,
                version_id = version.Id,
                reviewer_id = command.ReviewerId,
                state = version.State,
            },
            Reason: command.Comment), ct);
    }
}
