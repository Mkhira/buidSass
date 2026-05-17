namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Versioned, immutable snapshot of template body per
/// <c>data-model.md §notifications.template_versions</c>. Once published the
/// row is referenced by historical <see cref="Notification"/> rows for render
/// fidelity (BR-8 snapshot rule). V-1 publish gate enforces:
/// <c>ar_editorial_reviewed=true</c>, <c>reviewer_id != author_id</c>, and
/// locale-completeness (both AR + EN bodies non-empty per Principle 4).
/// </summary>
public sealed class TemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNo { get; set; }
    public string State { get; set; } = string.Empty;

    public string BodyAr { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;
    public string? SubjectAr { get; set; }
    public string? SubjectEn { get; set; }

    /// <summary>List of placeholder names (jsonb) extracted at draft time.</summary>
    public string PlaceholdersJson { get; set; } = "[]";

    public bool ArEditorialReviewed { get; set; }

    public Guid AuthorId { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? ReviewerComment { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
