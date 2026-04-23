using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.Identity.Primitives;

public static class RateLimitPolicies
{
    public const string CustomerOtpRequest = nameof(CustomerOtpRequest);
    public const string CustomerSignIn = nameof(CustomerSignIn);
    public const string AdminSignIn = nameof(AdminSignIn);
    public const string AdminOtpStepUp = nameof(AdminOtpStepUp);
    public const string PasswordResetRequest = nameof(PasswordResetRequest);
    public const string IdentifierItemKey = "__identity.rate-limit.identifier";

    private const string ScopeKeyItem = "__identity.rate-limit.scope-key";

    public static void RegisterAll(RateLimiterOptions options, IConfiguration configuration)
    {
        var partitionSecret = ResolvePartitionSecret(configuration);

        options.AddPolicy(CustomerOtpRequest, context =>
            BuildLimiter(
                context,
                permits: 3,
                window: TimeSpan.FromMinutes(10),
                policyCode: CustomerOtpRequest,
                partitionKey: ResolveCustomerOtpPartition(context, partitionSecret),
                surface: "customer"));

        options.AddPolicy(CustomerSignIn, context =>
            BuildLimiter(
                context,
                permits: 20,
                window: TimeSpan.FromHours(1),
                policyCode: CustomerSignIn,
                partitionKey: ResolveSignInPartition(context, partitionSecret),
                surface: "customer"));

        options.AddPolicy(AdminSignIn, context =>
            BuildLimiter(
                context,
                permits: 10,
                window: TimeSpan.FromHours(1),
                policyCode: AdminSignIn,
                partitionKey: ResolveSignInPartition(context, partitionSecret),
                surface: "admin"));

        options.AddPolicy(AdminOtpStepUp, context =>
            BuildLimiter(
                context,
                permits: 2,
                window: TimeSpan.FromHours(1),
                policyCode: AdminOtpStepUp,
                partitionKey: ResolveAdminStepUpPartition(context, partitionSecret),
                surface: "admin"));

        options.AddPolicy(PasswordResetRequest, context =>
            BuildLimiter(
                context,
                permits: 5,
                window: TimeSpan.FromHours(1),
                policyCode: PasswordResetRequest,
                partitionKey: ResolvePasswordResetPartition(context, partitionSecret),
                surface: "customer"));

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (rateLimitContext, cancellationToken) =>
        {
            try
            {
                var policyCode = ResolvePolicyCode(rateLimitContext.HttpContext);
                var scopeKey = rateLimitContext.HttpContext.Items.TryGetValue(ScopeKeyItem, out var item)
                    ? item as string ?? "unknown-scope"
                    : "unknown-scope";
                var surface = ResolveSurface(rateLimitContext.HttpContext);
                var sink = rateLimitContext.HttpContext.RequestServices.GetService(typeof(IRateLimitAuditSink)) as IRateLimitAuditSink;
                if (sink is not null)
                {
                    await sink.RecordRejectedAsync(policyCode, scopeKey, surface, cancellationToken);
                }
            }
            catch
            {
                // Rate limiting failures must not cascade into response-path failures.
            }
        };
    }

    private static RateLimitPartition<string> BuildLimiter(
        HttpContext context,
        int permits,
        TimeSpan window,
        string policyCode,
        string partitionKey,
        string surface)
    {
        var key = $"{policyCode}:{surface}:{partitionKey}";
        context.Items[ScopeKeyItem] = key;

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: key,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                SegmentsPerWindow = 4,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
    }

    private static string ResolveCustomerOtpPartition(HttpContext context, byte[] secret)
    {
        var normalizedPhone = ResolveIdentifier(context);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return $"ip:{ResolveIp(context)}";
        }

        return HashIdentifier(normalizedPhone, secret);
    }

    private static string ResolveAdminStepUpPartition(HttpContext context, byte[] secret)
    {
        var subject = context.User.FindFirst("sub")?.Value ?? "anonymous";
        if (!string.Equals(subject, "anonymous", StringComparison.Ordinal))
        {
            return HashIdentifier(subject, secret);
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            return HashIdentifier(authorization, secret);
        }

        return $"ip:{ResolveIp(context)}";
    }

    private static string ResolvePasswordResetPartition(HttpContext context, byte[] secret)
    {
        var normalizedEmail = ResolveIdentifier(context);
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return $"id:{HashIdentifier(normalizedEmail, secret)}";
        }

        return $"ip:{ResolveIp(context)}";
    }

    private static string ResolveSignInPartition(HttpContext context, byte[] secret)
    {
        var normalizedIdentifier = ResolveIdentifier(context);
        var ip = ResolveIp(context);
        return $"{HashIdentifier(normalizedIdentifier, secret)}:{ip}";
    }

    private static string ResolveIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
    }

    private static string ResolveIdentifier(HttpContext context)
    {
        if (context.Items.TryGetValue(IdentifierItemKey, out var value)
            && value is string identifier
            && !string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        return "missing-identifier";
    }

    private static string HashIdentifier(string value, byte[] secret)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = HMACSHA256.HashData(secret, bytes);
        return Convert.ToHexString(hash);
    }

    private static byte[] ResolvePartitionSecret(IConfiguration configuration)
    {
        var configured = configuration["Identity:RateLimiting:PartitionHmacKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Encoding.UTF8.GetBytes(configured);
        }

        return Encoding.UTF8.GetBytes("identity-rate-limiting-dev-key-only-change-me");
    }

    private static string ResolvePolicyCode(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        return metadata?.PolicyName ?? "unknown";
    }

    private static string ResolveSurface(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/v1/admin/", StringComparison.OrdinalIgnoreCase))
        {
            return "admin";
        }

        if (path.Contains("/v1/customer/", StringComparison.OrdinalIgnoreCase))
        {
            return "customer";
        }

        return "unknown";
    }
}
