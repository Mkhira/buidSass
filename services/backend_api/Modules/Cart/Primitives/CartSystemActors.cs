namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Deterministic sentinel actor GUIDs for cart operations initiated by the platform itself —
/// anonymous cart mutations, background worker cleanups, and similar. These are stable so audit
/// log readers can filter out "non-human" activity; none of them is a real account id.
/// </summary>
public static class CartSystemActors
{
    /// <summary>Anonymous customer (no logged-in account) — use when releasing reservations owned by a guest cart.</summary>
    public static readonly Guid Anonymous = Guid.Parse("00000000-aaaa-0000-0000-000000000001");

    public static readonly Guid AbandonmentWorker = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    public static readonly Guid GuestCleanupWorker = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2");
    public static readonly Guid ArchivedReaperWorker = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc3");

    public static Guid ResolveActor(Guid? accountId)
        => accountId is { } id && id != Guid.Empty ? id : Anonymous;
}
