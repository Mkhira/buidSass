namespace BackendApi.Modules.Shared;

/// <summary>
/// Publishes verification domain events to the in-process bus consumed by
/// spec 025 (Notifications) once it lands. Declared in <c>Modules/Shared/</c>
/// so spec 020's lifecycle workers + transition handlers can publish without
/// taking a hard dependency on spec 025's binding.
///
/// <para>FR-034 invariant: state-write paths MUST NOT block on publisher
/// failures. Implementations log and swallow; the verification transition
/// commits regardless. Subscriber catch-up runs out of band.</para>
/// </summary>
public interface IVerificationDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : class;
}
