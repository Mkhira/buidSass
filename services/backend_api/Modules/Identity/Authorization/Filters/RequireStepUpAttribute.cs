using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BackendApi.Modules.Identity.Authorization.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireStepUpAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var evaluator = services.GetRequiredService<PolicyEvaluator>();
        var emitter = services.GetRequiredService<IAuthorizationAuditEmitter>();

        var decision = evaluator.Evaluate(
            context.HttpContext.User,
            permissionCode: "identity.step_up",
            requiresStepUp: true);

        var accountIdRaw = context.HttpContext.User.FindFirst("sub")?.Value;
        var accountId = Guid.TryParse(accountIdRaw, out var parsedAccountId) ? parsedAccountId : (Guid?)null;

        await emitter.EmitDecisionAsync(
            new AuthorizationAuditDecision(
                accountId,
                string.Equals(context.HttpContext.User.FindFirst("surface")?.Value, "admin", StringComparison.OrdinalIgnoreCase)
                    ? Primitives.SurfaceKind.Admin
                    : Primitives.SurfaceKind.Customer,
                decision.PermissionCode,
                decision.IsAllowed ? "allow" : "deny",
                decision.ReasonCode,
                context.HttpContext.Items.TryGetValue("CorrelationId", out var correlationObject)
                && correlationObject is string correlationText
                && Guid.TryParse(correlationText, out var correlationId)
                    ? correlationId
                    : Guid.NewGuid()),
            context.HttpContext.RequestAborted);

        if (decision.IsAllowed)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status412PreconditionFailed,
            Title = "Step-up authentication required",
            Type = "https://errors.dental-commerce/identity/identity.step_up.required",
            Detail = "A recent MFA step-up is required for this operation.",
        };
        problem.Extensions["reasonCode"] = "identity.step_up.required";
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status412PreconditionFailed };
    }
}
