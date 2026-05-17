using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Notifications.Domain.StateMachines;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Templates;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Templates.Approve;

/// <summary>
/// T014 — approves an <c>in_review</c> <see cref="TemplateVersion"/> and
/// transitions it to <c>published</c>. Enforces V-1 publish gate at
/// handler entry:
/// <list type="bullet">
///   <item>Both AR and EN bodies must be non-empty (locale completeness).</item>
///   <item><c>reviewer_id != author_id</c> (single-author publishes are forbidden).</item>
///   <item><c>ar_editorial_reviewed</c> must be set to <c>true</c> in this
///         call (Principle 4 — only set by reviewer role at approve time).</item>
///   <item>No undeclared placeholders.</item>
/// </list>
/// On approval, the previous <c>published</c> version of the same template is
/// auto-archived and the template's <see cref="Domain.Template.CurrentVersionId"/>
/// is repointed at the new version (US3 acceptance #3).
/// </summary>
public sealed record ApproveCommand(
    Guid TemplateVersionId,
    Guid ReviewerId,
    bool ArEditorialReviewed) : IRequest;

public sealed class ApproveValidator : AbstractValidator<ApproveCommand>
{
    public ApproveValidator()
    {
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();
        RuleFor(x => x.ArEditorialReviewed).Equal(true)
            .WithMessage("V-1: ar_editorial_reviewed must be true at approve time (Principle 4).");
    }
}

public sealed class ApproveHandler : IRequestHandler<ApproveCommand>
{
    private readonly NotificationsDbContext _db;
    private readonly IValidator<ApproveCommand> _validator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public ApproveHandler(
        NotificationsDbContext db,
        IValidator<ApproveCommand> validator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public async Task Handle(ApproveCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var version = await _db.TemplateVersions
            .FirstOrDefaultAsync(v => v.Id == command.TemplateVersionId && v.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"TemplateVersion {command.TemplateVersionId} not found.");

        // V-1 publish gate. Each branch produces a precise, reviewer-actionable
        // error message rather than a generic 400.
        if (version.AuthorId == command.ReviewerId)
        {
            throw new InvalidOperationException("V-1: reviewer_id must differ from author_id.");
        }
        if (string.IsNullOrEmpty(version.BodyAr) || string.IsNullOrEmpty(version.BodyEn))
        {
            throw new InvalidOperationException("V-1: both ar and en bodies must be non-empty.");
        }

        // Re-validate placeholder declarations at publish time as defense in
        // depth — the body may have been edited via PATCH /templates/{id}
        // after the draft was created.
        var declared = JsonSerializer.Deserialize<string[]>(version.PlaceholdersJson) ?? Array.Empty<string>();
        PlaceholderValidator.EnsureNoUndeclaredPlaceholders(version.BodyAr, version.BodyEn, declared);

        TemplateVersionStateMachine.EnsureTransition(
            version.State, NotificationsConstants.TemplateVersionStates.Published);

        // Auto-archive the previous published version of the same template.
        var previousPublished = await _db.TemplateVersions
            .Where(v => v.TemplateId == version.TemplateId
                        && v.State == NotificationsConstants.TemplateVersionStates.Published
                        && v.Id != version.Id
                        && v.DeletedAt == null)
            .ToListAsync(ct);
        var now = _clock.GetUtcNow();
        foreach (var prev in previousPublished)
        {
            TemplateVersionStateMachine.EnsureTransition(
                prev.State, NotificationsConstants.TemplateVersionStates.Archived);
            prev.State = NotificationsConstants.TemplateVersionStates.Archived;
            prev.ArchivedAt = now;
            prev.UpdatedAt = now;
        }

        version.State = NotificationsConstants.TemplateVersionStates.Published;
        version.ReviewerId = command.ReviewerId;
        version.ArEditorialReviewed = true;
        version.PublishedAt = now;
        version.UpdatedAt = now;

        var template = await _db.Templates
            .FirstAsync(t => t.Id == version.TemplateId, ct);
        var previousVersionId = template.CurrentVersionId;
        template.CurrentVersionId = version.Id;
        template.State = NotificationsConstants.TemplateVersionStates.Published;
        template.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: command.ReviewerId,
            ActorRole: "template-reviewer",
            Action: "template.published",
            EntityType: "TemplateVersion",
            EntityId: version.Id,
            BeforeState: new { state = NotificationsConstants.TemplateVersionStates.InReview, previous_version_id = previousVersionId },
            AfterState: new
            {
                template_id = template.Id,
                version_id = version.Id,
                reviewer_id = command.ReviewerId,
                author_id = version.AuthorId,
                state = version.State,
                published_at = version.PublishedAt,
            },
            Reason: null), ct);
    }
}
