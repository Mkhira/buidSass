using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Templates.Archive;

/// <summary>
/// T016 — archives a <c>published</c> (or <c>draft</c> for cleanup) version.
/// Both <c>template-author</c> and <c>template-reviewer</c> roles can call
/// this. When archiving the current published version of a template, the
/// template's <c>current_version_id</c> is cleared to <c>null</c> so the
/// renderer fails fast rather than rendering a stale snapshot.
/// </summary>
public sealed record ArchiveCommand(Guid TemplateVersionId, Guid ActorId, string? Reason) : IRequest;

public sealed class ArchiveValidator : AbstractValidator<ArchiveCommand>
{
    public ArchiveValidator()
    {
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(4096);
    }
}

public sealed class ArchiveHandler : IRequestHandler<ArchiveCommand>
{
    private readonly NotificationsDbContext _db;
    private readonly IValidator<ArchiveCommand> _validator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public ArchiveHandler(
        NotificationsDbContext db,
        IValidator<ArchiveCommand> validator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public async Task Handle(ArchiveCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var version = await _db.TemplateVersions
            .FirstOrDefaultAsync(v => v.Id == command.TemplateVersionId && v.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"TemplateVersion {command.TemplateVersionId} not found.");

        TemplateVersionStateMachine.EnsureTransition(
            version.State, NotificationsConstants.TemplateVersionStates.Archived);

        var beforeState = version.State;
        var now = _clock.GetUtcNow();
        version.State = NotificationsConstants.TemplateVersionStates.Archived;
        version.ArchivedAt = now;
        version.UpdatedAt = now;

        var template = await _db.Templates
            .FirstAsync(t => t.Id == version.TemplateId, ct);
        if (template.CurrentVersionId == version.Id)
        {
            template.CurrentVersionId = null;
            template.State = NotificationsConstants.TemplateVersionStates.Archived;
            template.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: command.ActorId,
            ActorRole: "template-author",
            Action: "template.archived",
            EntityType: "TemplateVersion",
            EntityId: version.Id,
            BeforeState: new { state = beforeState },
            AfterState: new
            {
                template_id = version.TemplateId,
                version_id = version.Id,
                actor_id = command.ActorId,
                state = version.State,
                archived_at = version.ArchivedAt,
            },
            Reason: command.Reason), ct);
    }
}
