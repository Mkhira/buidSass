using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.Templates;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Templates.CreateDraft;

/// <summary>
/// T012 — creates a draft <see cref="Template"/> + <see cref="TemplateVersion"/>.
/// If a <see cref="Template"/> already exists for the given
/// <see cref="EventKind"/>, a new <see cref="TemplateVersion"/> is appended
/// with <c>version_no = max + 1</c> and <c>state='draft'</c>; otherwise both
/// rows are created. Placeholder declarations are normalized and validated
/// against the supplied bodies before persistence (US3 acceptance #1).
/// </summary>
public sealed record CreateDraftCommand(
    string EventKind,
    string BodyAr,
    string BodyEn,
    string? SubjectAr,
    string? SubjectEn,
    IReadOnlyList<string> Placeholders,
    Guid AuthorId) : IRequest<CreateDraftResponse>;

public sealed record CreateDraftResponse(Guid TemplateId, Guid VersionId, int VersionNo);

public sealed class CreateDraftValidator : AbstractValidator<CreateDraftCommand>
{
    public CreateDraftValidator()
    {
        RuleFor(x => x.EventKind).NotEmpty().MaximumLength(128);
        RuleFor(x => x.BodyAr).NotEmpty();
        RuleFor(x => x.BodyEn).NotEmpty();
        RuleFor(x => x.AuthorId).NotEmpty();
        RuleFor(x => x.Placeholders).NotNull();
    }
}

public sealed class CreateDraftHandler : IRequestHandler<CreateDraftCommand, CreateDraftResponse>
{
    private readonly NotificationsDbContext _db;
    private readonly IValidator<CreateDraftCommand> _validator;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _clock;

    public CreateDraftHandler(
        NotificationsDbContext db,
        IValidator<CreateDraftCommand> validator,
        IAuditEventPublisher audit,
        TimeProvider clock)
    {
        _db = db;
        _validator = validator;
        _audit = audit;
        _clock = clock;
    }

    public async Task<CreateDraftResponse> Handle(CreateDraftCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Reject drafts that reference placeholders they did not declare —
        // catches typos at edit time rather than render time.
        PlaceholderValidator.EnsureNoUndeclaredPlaceholders(
            command.BodyAr, command.BodyEn, command.Placeholders);

        var now = _clock.GetUtcNow();

        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.EventKind == command.EventKind && t.DeletedAt == null, ct);

        if (template is null)
        {
            template = new Template
            {
                Id = Guid.NewGuid(),
                EventKind = command.EventKind,
                State = NotificationsConstants.TemplateVersionStates.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Templates.Add(template);
        }
        else
        {
            template.UpdatedAt = now;
        }

        var nextVersionNo = await _db.TemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;

        var version = new TemplateVersion
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            VersionNo = nextVersionNo + 1,
            State = NotificationsConstants.TemplateVersionStates.Draft,
            BodyAr = command.BodyAr,
            BodyEn = command.BodyEn,
            SubjectAr = command.SubjectAr,
            SubjectEn = command.SubjectEn,
            PlaceholdersJson = JsonSerializer.Serialize(command.Placeholders),
            ArEditorialReviewed = false,
            AuthorId = command.AuthorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TemplateVersions.Add(version);

        await _db.SaveChangesAsync(ct);

        await _audit.PublishAsync(new AuditEvent(
            ActorId: command.AuthorId,
            ActorRole: "template-author",
            Action: "template.draft_created",
            EntityType: "TemplateVersion",
            EntityId: version.Id,
            BeforeState: null,
            AfterState: new
            {
                template_id = template.Id,
                event_kind = template.EventKind,
                version_no = version.VersionNo,
                placeholders = command.Placeholders,
            },
            Reason: null), ct);

        return new CreateDraftResponse(template.Id, version.Id, version.VersionNo);
    }
}
