using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Modules.Pricing.Admin.Common;

/// <summary>
/// Spec 007-b commercial-authoring shared response helpers + RBAC reads.
///
/// Shape parallels <see cref="AdminPricingResponseFactory"/> (used by the existing 007-a
/// admin endpoints) but namespaces the error type URI under <c>commercial</c> per
/// contract §1 / §11. Centralising RBAC reads keeps the per-endpoint code free of
/// claim-string drift.
/// </summary>
public static class AdminCommercialResponseFactory
{
    /// <summary>
    /// Build a Problem Details response keyed on a stable <c>reason_code</c>
    /// (contract §11). Optional <paramref name="extensions"/> let handlers attach
    /// machine-readable detail (overlap rule ids, current row body for 409s, etc.).
    /// </summary>
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
            Detail = detail,
            Type = $"https://errors.dental-commerce/commercial/{reasonCode}",
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

    /// <summary>
    /// Resolve the calling actor's account id from the Admin JWT subject claim.
    /// Returns <see cref="Guid.Empty"/> when no claim is present (test scenarios)
    /// rather than throwing — endpoints already gate on auth before reaching here.
    /// </summary>
    public static Guid ResolveActorAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Resolve the calling actor's role for audit-row tagging. The Admin JWT
    /// surface (spec 004) does not embed permission claims, so the role is
    /// inferred from the endpoint's <see cref="RequirePermissionMetadata"/>:
    /// the route's gate-permission IS the actor's effective role for this call.
    /// Endpoint authors pass the role explicitly via <see cref="ResolveActorRole(HttpContext, string)"/>
    /// to keep audit rows consistent with the contract §1 RBAC table.
    /// </summary>
    public static string ResolveActorRole(HttpContext context, string fallbackRoleCode)
    {
        // The PolicyEvaluator deny-path can stamp the resolved role onto items
        // before reaching the handler; if not present we use the route-declared
        // permission as the role tag (the route enforces the chord upstream).
        return context.Items.TryGetValue("commercial.actor_role", out var value) && value is string s
            ? s
            : fallbackRoleCode;
    }

    /// <summary>
    /// Parse the optional <c>If-Match</c> header into a <see cref="uint"/> xmin row-version.
    /// Header absence yields a null with <c>parsedOk=true</c> (no guard requested);
    /// a malformed header yields <c>parsedOk=false</c>. Endpoints treat the latter as a
    /// version-conflict signal so a corrupted header never silently disables the guard.
    /// </summary>
    public static (bool ParsedOk, uint? IfMatch) TryParseIfMatch(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("If-Match", out var raw) ||
            string.IsNullOrWhiteSpace(raw.ToString()))
        {
            return (true, null);
        }
        var trimmed = raw.ToString().Trim().Trim('"');
        return uint.TryParse(trimmed, out var parsed) ? (true, parsed) : (false, null);
    }
}
