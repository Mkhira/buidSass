using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// No-op fallback for <see cref="IVerificationDomainEventPublisher"/> until
/// spec 025 (Notifications) ships. Lives in this assembly so production has a
/// safe default binding.
/// </summary>
public sealed class NullVerificationDomainEventPublisher : IVerificationDomainEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : class
        => Task.CompletedTask;
}
