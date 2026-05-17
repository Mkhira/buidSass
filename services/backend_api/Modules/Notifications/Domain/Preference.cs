namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Customer channel / category opt-in per
/// <c>data-model.md §notifications.preferences</c>. Composite PK
/// <c>(customer_id, channel, category)</c>. V-4: a row with
/// <c>category='transactional'</c> CANNOT be set to <c>enabled=false</c>; the
/// rule is enforced at the app layer (PreferenceUpdate handler) and via a DB
/// trigger as defense-in-depth.
/// </summary>
public sealed class Preference
{
    public Guid CustomerId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
