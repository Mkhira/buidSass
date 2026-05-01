using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Cms;

/// <summary>
/// Per-module response shaping for the CMS HTTP surface. Problem-Details
/// errors with a stable <c>reasonCode</c> extension; JWT-claim resolution
/// for the authenticated admin actor. Mirrors the Reviews / Verification
/// per-module helpers.
/// </summary>
public static class CmsResponseFactory
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
            Type = $"https://errors.dental-commerce/cms/{reasonCode}",
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

    public static string ResolveActorRole(HttpContext context) =>
        context.User.FindFirst("role")?.Value
        ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
        ?? "unknown";

    /// <summary>
    /// True if the JWT carries a <c>permissions</c> array claim that contains
    /// <paramref name="permission"/>. Mirrors <c>AdminReviewsResponseFactory</c>.
    /// </summary>
    public static bool HasPermissionClaim(HttpContext context, string permission)
    {
        foreach (var claim in context.User.Claims)
        {
            if (claim.Type != "permissions" && claim.Type != "permission") continue;
            // permissions claim may be a single string or a JSON array.
            if (string.Equals(claim.Value, permission, StringComparison.Ordinal)) return true;
            if (claim.Value.StartsWith('[') && claim.Value.Contains($"\"{permission}\"", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
