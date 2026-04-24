using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Cart.Customer.Common;

public static class CustomerCartResponseFactory
{
    /// <summary>
    /// Postgres unique-violation (23505) is the signature of a concurrent insert race on either
    /// the partial unique index over (account, market, active) or on (cart_id, product_id).
    /// Cart endpoints catch this and surface a 409 so clients retry; see FR-012 / spec R9.
    /// </summary>
    public const string PostgresUniqueViolation = "23505";

    public static bool IsConcurrencyConflict(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException)
        {
            return true;
        }
        return ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolation;
    }

    public static IResult ConcurrencyConflict(HttpContext context, string detail)
        => Problem(context, StatusCodes.Status409Conflict, "cart.concurrency_conflict",
            "Concurrent modification", detail);

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

    public static Guid? ResolveAccountId(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Optionally authenticates the request as CustomerJwt so the endpoint can treat the caller
    /// as logged-in when a valid bearer token is present, or fall back to anonymous resolution
    /// when it's absent. Cart routes accept both; neither a missing nor an invalid token should
    /// block the request path.
    /// </summary>
    public static async Task<Guid?> TryResolveAuthenticatedAccountAsync(HttpContext context)
    {
        var cached = ResolveAccountId(context);
        if (cached is not null)
        {
            return cached;
        }
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            return null;
        }
        var auth = await context.AuthenticateAsync("CustomerJwt");
        if (!auth.Succeeded || auth.Principal is null)
        {
            return null;
        }
        context.User = auth.Principal;
        return ResolveAccountId(context);
    }
}
