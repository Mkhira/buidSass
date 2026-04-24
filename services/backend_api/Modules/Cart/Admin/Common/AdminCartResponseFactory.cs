using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Cart.Admin.Common;

public static class AdminCartResponseFactory
{
    public static IResult Problem(
        HttpContext context,
        int statusCode,
        string reasonCode,
        string title,
        string detail,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://errors.dental-commerce/cart/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    /// <summary>
    /// Extracts the admin's account id from the JWT claims. For admin endpoints the presence of
    /// a valid bearer token is enforced upstream by the `AdminJwt` scheme, so any caller that
    /// reaches this factory without a parseable `sub` claim is a misconfiguration — we fail
    /// fast so downstream audit writes can't silently emit `Guid.Empty` as the actor
    /// (AuditEventPublisher.Validate rejects it and throws mid-request).
    /// </summary>
    public static Guid ResolveActorAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var id) && id != Guid.Empty)
        {
            return id;
        }
        throw new UnauthorizedAccessException(
            "Admin endpoint invoked without a resolvable actor account id (missing or invalid 'sub' claim).");
    }
}
