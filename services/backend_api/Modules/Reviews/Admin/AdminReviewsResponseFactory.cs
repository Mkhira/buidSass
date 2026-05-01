using BackendApi.Modules.Reviews.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Reviews.Admin;

/// <summary>
/// Admin-side response shaping for the Reviews module. Mirrors the Verification
/// admin pattern: Problem-Details errors with a stable <c>reasonCode</c>
/// extension and JWT claim resolution helpers for the admin actor.
/// </summary>
public static class AdminReviewsResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail ?? string.Empty,
            Type = $"https://errors.dental-commerce/reviews/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
            {
                problem.Extensions[k] = v;
            }
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid? ResolveActorId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public static bool HasModeratorPermission(HttpContext context) =>
        HasPermissionClaim(context, ReviewsPermissions.Moderator);

    public static bool HasSuperAdmin(HttpContext context) =>
        HasPermissionClaim(context, "super_admin");

    /// <summary>
    /// True when the user has the named permission via either the singular
    /// <c>permission</c> or repeated <c>permissions</c> claim shape — matching
    /// VerificationModule's admin-auth convention.
    /// </summary>
    public static bool HasPermissionClaim(HttpContext context, string permission) =>
        context.User.HasClaim("permission", permission)
        || context.User.HasClaim("permissions", permission)
        || context.User.HasClaim("perm", permission);
}
