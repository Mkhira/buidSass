namespace BackendApi.Modules.Cms.Storefront;

/// <summary>
/// Marker interface implemented by the 5 CMS entity classes so the leak-safe
/// <see cref="StorefrontContentResolver"/> can apply a uniform live + window
/// + market+locale tier-sort filter across all storefront read endpoints.
/// Per spec 024 research §R13.
/// </summary>
public interface ICmsContentRow
{
    /// <summary>Wire form of <see cref="Primitives.ContentLifecycleState"/>.</summary>
    string StateWire { get; }
    string MarketCode { get; }

    /// <summary>Banner-style window start; null when the kind has only <see cref="ScheduledPublishAtUtc"/>.</summary>
    DateTimeOffset? ScheduledStartUtc { get; }

    /// <summary>Banner-style window end; null when the kind has only <see cref="ScheduledPublishAtUtc"/>.</summary>
    DateTimeOffset? ScheduledEndUtc { get; }

    /// <summary>Single-point publish at; null when the kind uses a window.</summary>
    DateTimeOffset? ScheduledPublishAtUtc { get; }
}
