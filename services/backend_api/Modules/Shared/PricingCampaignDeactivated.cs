using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// INBOUND notification published by spec 007-b's pricing module when a
/// campaign is deactivated. Spec 024's CMS module subscribes to release any
/// active <c>BannerCampaignBinding</c> rows pointing at the campaign.
/// Declared here in <c>Modules/Shared/</c> so neither side has a hard
/// dependency on the other (loose-coupling pattern from specs 020/021/022).
/// </summary>
public sealed record PricingCampaignDeactivated(
    Guid CampaignId,
    DateTimeOffset DeactivatedAtUtc) : INotification;
