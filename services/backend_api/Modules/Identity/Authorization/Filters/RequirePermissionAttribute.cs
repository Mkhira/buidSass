using BackendApi.Modules.Identity.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BackendApi.Modules.Identity.Authorization.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permissionCode, string? requiredMarketCode = null)
    : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var evaluator = services.GetRequiredService<PolicyEvaluator>();
        var emitter = services.GetRequiredService<IAuthorizationAuditEmitter>();

        var decision = evaluator.Evaluate(context.HttpContext.User, permissionCode, requiredMarketCode, requiresStepUp: false);
        await EmitAsync(context, decision, emitter, cancellationToken: context.HttpContext.RequestAborted);

        if (decision.IsAllowed)
        {
            return;
        }

        context.Result = BuildDenyResult(decision, StatusCodes.Status403Forbidden);
    }

    private static async Task EmitAsync(
        AuthorizationFilterContext context,
        AuthorizationPolicyDecision decision,
        IAuthorizationAuditEmitter emitter,
        CancellationToken cancellationToken)
    {
        var accountId = context.HttpContext.User.FindFirst("sub")?.Value;
        var accountGuid = Guid.TryParse(accountId, out var parsedAccountId) ? parsedAccountId : (Guid?)null;
        var correlationRaw = context.HttpContext.Items.TryGetValue("CorrelationId", out var correlationObject)
            ? correlationObject as string
            : null;

        var correlationId = Guid.TryParse(correlationRaw, out var parsedCorrelationId)
            ? parsedCorrelationId
            : Guid.NewGuid();

        var surface = ParseSurface(context.HttpContext.User.FindFirst("surface")?.Value);
        await emitter.EmitDecisionAsync(
            new AuthorizationAuditDecision(
                accountGuid,
                surface,
                decision.PermissionCode,
                decision.IsAllowed ? "allow" : "deny",
                decision.ReasonCode,
                correlationId),
            cancellationToken);
    }

    private static SurfaceKind ParseSurface(string? raw)
    {
        return string.Equals(raw, "admin", StringComparison.OrdinalIgnoreCase)
            ? SurfaceKind.Admin
            : SurfaceKind.Customer;
    }

    private static ObjectResult BuildDenyResult(AuthorizationPolicyDecision decision, int statusCode)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "Authorization denied",
            Detail = $"Permission check failed: {decision.PermissionCode}",
            Type = $"https://errors.dental-commerce/identity/{decision.ReasonCode}",
        };

        problem.Extensions["reasonCode"] = decision.ReasonCode;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
