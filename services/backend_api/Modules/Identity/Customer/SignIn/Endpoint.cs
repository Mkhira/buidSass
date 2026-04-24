using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Customer.Common;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Identity.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

namespace BackendApi.Modules.Identity.Customer.SignIn;

public static class CustomerSignInEndpoint
{
    public static IEndpointRouteBuilder MapCustomerSignInEndpoint(this IEndpointRouteBuilder builder)
    {
        builder
            .MapPost("/sign-in", HandleAsync)
            .RequireRateLimiting(RateLimitPolicies.CustomerSignIn);

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CustomerSignInRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        Argon2idHasher hasher,
        CustomerAuthSessionService authSessionService,
        IAuditEventPublisher auditEventPublisher,
        IEnumerable<ICustomerPostSignInHook> postSignInHooks,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validator = new CustomerSignInRequestValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            await ConstantTimeOperation.EnsureMinimumDurationAsync(
                startedAt,
                TimeSpan.FromMilliseconds(500),
                cancellationToken);
            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "identity.sign_in.invalid_request",
                "Invalid sign-in request",
                validation.Errors.First().ErrorMessage);
        }

        var result = await CustomerSignInHandler.HandleAsync(
            request,
            httpContext,
            dbContext,
            hasher,
            authSessionService,
            auditEventPublisher,
            cancellationToken);

        await ConstantTimeOperation.EnsureMinimumDurationAsync(
            startedAt,
            TimeSpan.FromMilliseconds(500),
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.LockedUntil is DateTimeOffset lockedUntil)
            {
                return CustomerIdentityResponseFactory.Problem(
                    httpContext,
                    result.StatusCode,
                    result.ReasonCode!,
                    result.Title!,
                    result.Detail!,
                    new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil });
            }

            return CustomerIdentityResponseFactory.Problem(
                httpContext,
                result.StatusCode,
                result.ReasonCode!,
                result.Title!,
                result.Detail!);
        }

        await InvokePostSignInHooksAsync(
            result.Session!,
            httpContext,
            dbContext,
            postSignInHooks,
            loggerFactory.CreateLogger("Identity.PostSignIn"),
            cancellationToken);

        return Results.Ok(result.Session);
    }

    private static async Task InvokePostSignInHooksAsync(
        AuthSessionResponse session,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IEnumerable<ICustomerPostSignInHook> hooks,
        ILogger logger,
        CancellationToken ct)
    {
        var hookList = hooks.ToList();
        if (hookList.Count == 0)
        {
            return;
        }

        try
        {
            // JWT is already signed — we only need to read `sub` to learn the accountId for hooks.
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(session.AccessToken);
            var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(subClaim, out var accountId))
            {
                return;
            }

            var marketCode = await dbContext.Accounts
                .AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => a.MarketCode)
                .SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(marketCode))
            {
                return;
            }

            var cartToken = ResolveCartToken(httpContext);
            var hookContext = new CustomerPostSignInContext(accountId, marketCode, cartToken);

            foreach (var hook in hookList)
            {
                try
                {
                    await hook.OnSignedInAsync(hookContext, ct);
                }
                catch (Exception ex)
                {
                    // Hooks MUST NOT abort the sign-in. Log and continue to the next hook.
                    logger.LogWarning(ex, "identity.post_sign_in_hook.failed hook={Hook}", hook.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "identity.post_sign_in_hooks.dispatch_failed");
        }
    }

    private static string? ResolveCartToken(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Cart-Token", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }
        if (context.Request.Cookies.TryGetValue("cart_token", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
        {
            return cookie;
        }
        return null;
    }
}
