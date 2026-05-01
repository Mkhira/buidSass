namespace BackendApi.Modules.Shared;

/// <summary>
/// Subscriber interface that downstream modules implement to react to spec
/// 004 role-lifecycle events. Spec 024 CMS uses this to flag drafts whose
/// owner's <c>cms.editor</c> role was revoked (FR-034a).
/// </summary>
/// <remarks>
/// If spec 004 ships an authoritative declaration of this contract elsewhere,
/// the duplicate here is harmless — both interfaces will live in the same
/// namespace and assembly. Until then, 024 owns the declaration.
/// </remarks>
public interface ICustomerRoleLifecycleSubscriber
{
    Task OnRoleRevokedAsync(Guid actorId, string roleCode, CancellationToken ct);
}
