namespace BackendApi.Modules.Observability;

public sealed class CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.Remove("X-Correlation-Id");
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string ResolveCorrelationId()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return Guid.NewGuid().ToString("D");
        }

        if (ctx.Items.TryGetValue("CorrelationId", out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("D");
    }
}
