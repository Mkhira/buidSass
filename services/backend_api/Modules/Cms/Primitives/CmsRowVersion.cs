namespace BackendApi.Modules.Cms.Primitives;

/// <summary>
/// Typed wrapper for the EF Core <c>xmin</c> row_version on every CMS entity.
/// Used at the slice boundary to make optimistic-concurrency intent explicit.
/// </summary>
public readonly record struct CmsRowVersion(uint Value)
{
    public static CmsRowVersion FromRaw(uint raw) => new(raw);
    public uint ToRaw() => Value;

    public bool Matches(uint actual) => actual == Value;
}
