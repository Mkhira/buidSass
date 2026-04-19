using Serilog.Context;

namespace BackendApi.Modules.Observability;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString("D") : incoming;

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
