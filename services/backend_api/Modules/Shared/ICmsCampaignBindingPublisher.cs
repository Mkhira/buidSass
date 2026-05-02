using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// OUTBOUND publisher signature for spec 024 banner ↔ campaign binding events.
/// Spec 007-b's deactivation flow consumes via the in-process MediatR bus on
/// the <c>pricing.campaign.deactivated</c> event; this interface is the
/// contractual outbound surface 024 commits to.
/// </summary>
public interface ICmsCampaignBindingPublisher
{
    Task PublishBoundAsync(CmsBannerCampaignBound notification, CancellationToken ct);
    Task PublishUnboundAsync(CmsBannerCampaignUnbound notification, CancellationToken ct);
}

public sealed record CmsBannerCampaignBound(
    Guid BannerId,
    Guid CampaignId,
    string MarketCode,
    Guid ActorId,
    DateTimeOffset BoundAtUtc) : INotification;

public sealed record CmsBannerCampaignUnbound(
    Guid BannerId,
    Guid CampaignId,
    string ReleaseReasonWire,
    Guid? ActorId,
    DateTimeOffset ReleasedAtUtc) : INotification;
