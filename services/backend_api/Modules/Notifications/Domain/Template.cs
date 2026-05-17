namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Notification template aggregate per <c>data-model.md §notifications.templates</c>.
/// One row per <c>event_kind</c>. <see cref="CurrentVersionId"/> references the
/// active <see cref="TemplateVersion"/> snapshot used at render time. State is
/// derived from the active version's state.
/// </summary>
public sealed class Template
{
    public Guid Id { get; set; }
    public string EventKind { get; set; } = string.Empty;
    public Guid? CurrentVersionId { get; set; }
    public string State { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
