namespace BackendApi.Modules.Reviews.Authorization;

/// <summary>
/// Permission constants for the Reviews module. Surfaced as
/// <c>[RequirePermission(ReviewsPermissions.X)]</c> on slice endpoints; spec
/// 015 (admin-foundation) wires the role bindings on its PR.
/// </summary>
public static class ReviewsPermissions
{
    public const string Moderator = "reviews.moderator";
    public const string PolicyAdmin = "reviews.policy_admin";
}
