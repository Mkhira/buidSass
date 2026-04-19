using BackendApi.Modules.Observability;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace backend_api.Tests.Observability;

public sealed class CorrelationIdTests
{
    [Fact]
    public async Task Middleware_Pushes_CorrelationId_Into_All_Log_Events_When_Header_Exists()
    {
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "test-abc-123";

        var middleware = new CorrelationIdMiddleware(_ =>
        {
            Log.Information("log-entry-1");
            Log.Information("log-entry-2");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("test-abc-123", context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, evt =>
        {
            Assert.True(evt.Properties.TryGetValue("CorrelationId", out var value));
            Assert.Contains("test-abc-123", value.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Middleware_Generates_CorrelationId_When_Header_Missing()
    {
        var context = new DefaultHttpContext();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var value = context.Response.Headers["X-Correlation-Id"].ToString();
        Assert.True(Guid.TryParse(value, out _));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
