using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Checkout.Customer.Common;

public static class CustomerCheckoutResponseFactory
{
    public const string PostgresUniqueViolation = "23505";

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
            Type = $"https://errors.dental-commerce/checkout/{reasonCode}",
            Instance = context.Request.Path,
        };
        problem.Extensions["reasonCode"] = reasonCode;
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    public static Guid? ResolveAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Optionally authenticates via CustomerJwt so pre-submit endpoints can surface the
    /// logged-in account without forcing auth (spec 010 FR-019 — guest may fill session,
    /// only `submit` rejects guests).
    /// </summary>
    public static async Task<Guid?> TryResolveAuthenticatedAccountAsync(HttpContext context)
    {
        var cached = ResolveAccountId(context);
        if (cached is not null) return cached;
        if (!context.Request.Headers.ContainsKey("Authorization")) return null;
        var auth = await context.AuthenticateAsync("CustomerJwt");
        if (!auth.Succeeded || auth.Principal is null) return null;
        context.User = auth.Principal;
        return ResolveAccountId(context);
    }

    public static bool IsConcurrencyConflict(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException) return true;
        return ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolation;
    }
}
