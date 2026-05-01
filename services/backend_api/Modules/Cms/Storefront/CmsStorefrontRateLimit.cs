using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Cms.Storefront;

/// <summary>
/// Per-IP partitioned rate-limit policy for the storefront CMS endpoints
/// (FR-031, contract §7). V1 default 600 req/min/IP per <c>entity_kind</c>;
/// configurable per environment via <c>Cms:Storefront:RateLimit:*</c>
/// configuration keys. Activated by <c>app.UseRateLimiter()</c> in the host;
/// the per-endpoint <c>RequireRateLimiting</c> attaches the named policy.
/// </summary>
public static class CmsStorefrontRateLimit
{
    public const string PolicyName = "cms-storefront";

    public static IServiceCollection AddCmsStorefrontRateLimit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rpm = int.TryParse(configuration["Cms:Storefront:RateLimit:RequestsPerMinute"], out var v) && v > 0
            ? v : 600;

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyName, httpContext =>
            {
                var partitionKey = ResolvePartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rpm,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Partition by client IP × <c>entity_kind</c> (the route group's first
    /// path segment after <c>/v1/storefront/cms/</c>). Falls back to the path
    /// itself when the segment is absent.
    /// </summary>
    private static string ResolvePartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value ?? string.Empty;
        const string prefix = "/v1/storefront/cms/";
        var entityKind = path.StartsWith(prefix, StringComparison.Ordinal)
            ? path[prefix.Length..].Split('/', 2)[0]
            : path;
        return $"{ip}|{entityKind}";
    }
}
