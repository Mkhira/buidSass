namespace BackendApi.Modules.Identity.Authorization.Filters;

public sealed class RequireStepUpEndpointFilter(
    PolicyEvaluator policyEvaluator,
    IAuthorizationAuditEmitter authorizationAuditEmitter) : IEndpointFilter
{
    private readonly PolicyEvaluator _policyEvaluator = policyEvaluator;
    private readonly IAuthorizationAuditEmitter _authorizationAuditEmitter = authorizationAuditEmitter;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequireStepUpMetadata>();
        if (metadata is null)
        {
            return await next(context);
        }

        var permissionMetadata = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequirePermissionMetadata>();
        var permissionCode = metadata.PermissionCode
                             ?? permissionMetadata?.PermissionCode
                             ?? "identity.step_up";

        var decision = await _policyEvaluator.EvaluateAsync(
            context.HttpContext.User,
            permissionCode,
            requiresStepUp: true,
            cancellationToken: context.HttpContext.RequestAborted);

        await _authorizationAuditEmitter.EmitDecisionAsync(
            new AuthorizationAuditDecision(
                EndpointAuthorizationFilterHelpers.ResolveAccountId(context.HttpContext.User),
                EndpointAuthorizationFilterHelpers.ResolveSurface(context.HttpContext.User),
                decision.PermissionCode,
                decision.IsAllowed ? "allow" : "deny",
                decision.ReasonCode,
                EndpointAuthorizationFilterHelpers.ResolveCorrelationId(context.HttpContext)),
            context.HttpContext.RequestAborted);

        if (!decision.IsAllowed)
        {
            return EndpointAuthorizationFilterHelpers.BuildDenyResult(
                context.HttpContext,
                decision,
                stepUpFailure: string.Equals(decision.ReasonCode, "mfa_not_satisfied", StringComparison.Ordinal));
        }

        return await next(context);
    }
}
