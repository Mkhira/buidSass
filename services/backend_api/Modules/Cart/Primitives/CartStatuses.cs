namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Allowed values for cart.carts.Status (Principle 24 — explicit state machine). The column is
/// citext so values are stored lower-case. Database CHECK constraint enforces the enum at the
/// schema level; this class centralizes the values + legal transitions for the application.
/// </summary>
public static class CartStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
    public const string Merged = "merged";
    public const string Purged = "purged";

    /// <summary>
    /// Allowed transitions per spec 009 data-model.md state machine. Callers that mutate
    /// `cart.Status` should route through <see cref="TryTransition"/> so invalid transitions
    /// are rejected at the boundary rather than caught later at SaveChanges time.
    /// </summary>
    public static bool IsValidTransition(string from, string to) => (from, to) switch
    {
        (Active, Archived) => true,     // market switch / superseded
        (Active, Merged) => true,       // anon → auth login merge
        (Active, Purged) => true,       // 30-day guest cleanup
        (Archived, Active) => true,     // restore within retention window
        (Archived, Purged) => true,     // archive reaper
        _ => false,
    };

    public static bool TryTransition(BackendApi.Modules.Cart.Entities.Cart cart, string target, string? reason, DateTimeOffset nowUtc)
    {
        if (!IsValidTransition(cart.Status, target))
        {
            return false;
        }
        cart.Status = target;
        if (target is Archived or Merged or Purged)
        {
            cart.ArchivedAt ??= nowUtc;
            cart.ArchivedReason = reason ?? cart.ArchivedReason;
        }
        else if (target == Active)
        {
            cart.ArchivedAt = null;
            cart.ArchivedReason = null;
        }
        cart.UpdatedAt = nowUtc;
        return true;
    }
}
