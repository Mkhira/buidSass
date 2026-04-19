using BackendApi.Modules.AuditLog;
using MediatR;

namespace backend_api.Tests.AuditLog;

public sealed class FiveLineCallingCodeTest
{
    [Fact]
    public async Task MediatR_Handler_Publish_Call_Fits_Five_Line_Criterion()
    {
        var fakePublisher = new FakeAuditPublisher();
        var handler = new MinimalHandler(fakePublisher);

        await handler.Handle(new MinimalCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(fakePublisher.Called);
    }

    public sealed record MinimalCommand(Guid EntityId) : IRequest;

    // The PublishAsync call path inside Handle is intentionally tiny (<=5 lines).
    private sealed class MinimalHandler(IAuditEventPublisher publisher) : IRequestHandler<MinimalCommand>
    {
        public async Task Handle(MinimalCommand request, CancellationToken cancellationToken)
        {
            await publisher.PublishAsync(new AuditEvent(Guid.NewGuid(), "aw", "entity.updated", "Entity", request.EntityId, null, new { ok = true }, null), cancellationToken);
        }
    }

    private sealed class FakeAuditPublisher : IAuditEventPublisher
    {
        public bool Called { get; private set; }

        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
