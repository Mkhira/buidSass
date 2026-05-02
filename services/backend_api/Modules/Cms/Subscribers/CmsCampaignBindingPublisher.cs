using BackendApi.Modules.Shared;
using MediatR;

namespace BackendApi.Modules.Cms.Subscribers;

/// <summary>
/// CMS-side implementation of <see cref="ICmsCampaignBindingPublisher"/>.
/// Forwards to the in-process MediatR bus so spec 007-b's handlers can
/// subscribe via the bus channel.
/// </summary>
public sealed class CmsCampaignBindingPublisher : ICmsCampaignBindingPublisher
{
    private readonly IPublisher _bus;

    public CmsCampaignBindingPublisher(IPublisher bus)
    {
        _bus = bus;
    }

    public Task PublishBoundAsync(CmsBannerCampaignBound notification, CancellationToken ct) =>
        _bus.Publish(notification, ct);

    public Task PublishUnboundAsync(CmsBannerCampaignUnbound notification, CancellationToken ct) =>
        _bus.Publish(notification, ct);
}
