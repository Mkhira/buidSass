namespace BackendApi.Modules.Shared;

/// <summary>
/// Reads the display fields used by the canonical reviewer-display rule
/// (FR-016a). Spec 019 (customer profile) implements. Spec 022 falls back to
/// first-name + last-initial when <see cref="CustomerDisplayInfo.ReviewDisplayHandle"/>
/// is <see langword="null"/>.
/// </summary>
public interface IReviewDisplayHandleQuery
{
    Task<CustomerDisplayInfo?> GetAsync(Guid customerId, CancellationToken ct);
}

public sealed record CustomerDisplayInfo(
    string FirstName,
    string LastName,
    string? ReviewDisplayHandle);
