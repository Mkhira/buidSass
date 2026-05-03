namespace BackendApi.Modules.Pricing.Primitives.Commercial;

/// <summary>
/// Actor classification for commercial-authoring audit + RBAC traces (spec 007-b T011).
/// </summary>
public enum CommercialActorKind
{
    Operator = 0,
    B2BAuthor = 1,
    Approver = 2,
    SuperAdmin = 3,
    System = 4,
}
