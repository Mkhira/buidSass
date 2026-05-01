namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Append-only operator-side note attached to a review per data-model §2.3.
/// Visible to moderators + support; never customer-visible.
/// </summary>
public sealed class ReviewAdminNote
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid ActorId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
