using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cms.Services;

/// <summary>
/// Validates a banner's CTA target via the cross-module catalog read contracts
/// per spec 024 research §R9. Two operating modes:
///
/// <list type="bullet">
///   <item><b>Publish path</b> (<see cref="ValidateForPublishAsync"/>) — hard-fails
///         on unresolvable CTA, throwing <see cref="BannerCtaTargetUnresolvableException"/>.</item>
///   <item><b>Read path</b> (<see cref="ResolveHealthAsync"/>) — fail-open on transient
///         catalog errors with <see cref="CtaHealth.TransientUnverified"/>.</item>
/// </list>
///
/// CTA kinds <c>none</c>, <c>link</c>, <c>external_url</c> are validated by
/// <see cref="Editor.SaveBannerDraft.SaveBannerDraftValidator"/> at save time;
/// this validator is concerned only with kinds that resolve against the
/// catalog (<c>product</c> / <c>category</c> / <c>bundle</c>).
/// </summary>
public sealed class BannerCtaValidator
{
    private readonly ICatalogProductReadContract _products;
    private readonly ICatalogCategoryReadContract _categories;
    private readonly ICatalogBundleReadContract _bundles;
    private readonly ILogger<BannerCtaValidator> _log;

    public BannerCtaValidator(
        ICatalogProductReadContract products,
        ICatalogCategoryReadContract categories,
        ICatalogBundleReadContract bundles,
        ILogger<BannerCtaValidator> log)
    {
        _products = products;
        _categories = categories;
        _bundles = bundles;
        _log = log;
    }

    /// <summary>
    /// Hard-fail path used at publish-now / schedule-publish / worker promotion.
    /// Returns the resolved <see cref="CtaHealth"/>; throws on unresolvable CTAs.
    /// </summary>
    public async Task<CtaHealth> ValidateForPublishAsync(BannerSlot row, CancellationToken ct)
    {
        if (!IsCatalogBound(row.CtaKindWire)) return CtaHealth.NotApplicable;

        if (string.IsNullOrWhiteSpace(row.CtaTarget) || !Guid.TryParse(row.CtaTarget, out var targetId))
        {
            throw new BannerCtaTargetUnresolvableException(row.Id, row.CtaKindWire, row.CtaTarget);
        }

        var available = await TryResolveAsync(row.CtaKindWire, targetId, row.MarketCode, ct);
        if (available is null)
        {
            // Transient error → fail-closed at publish time.
            throw new BannerCtaTargetUnresolvableException(row.Id, row.CtaKindWire, row.CtaTarget);
        }

        return available.Value ? CtaHealth.Verified : throw new BannerCtaTargetUnresolvableException(
            row.Id, row.CtaKindWire, row.CtaTarget);
    }

    /// <summary>
    /// Fail-open path used at storefront read. Returns the resolved
    /// <see cref="CtaHealth"/> for the row; never throws.
    /// </summary>
    public async Task<CtaHealth> ResolveHealthAsync(BannerSlot row, CancellationToken ct)
    {
        if (!IsCatalogBound(row.CtaKindWire)) return CtaHealth.NotApplicable;

        if (string.IsNullOrWhiteSpace(row.CtaTarget) || !Guid.TryParse(row.CtaTarget, out var targetId))
        {
            return CtaHealth.Broken;
        }

        try
        {
            var available = await TryResolveAsync(row.CtaKindWire, targetId, row.MarketCode, ct);
            if (available is null) return CtaHealth.TransientUnverified;
            return available.Value ? CtaHealth.Verified : CtaHealth.Broken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Banner CTA resolution failed transiently for banner {BannerId} ({Kind}/{Target})",
                row.Id, row.CtaKindWire, row.CtaTarget);
            return CtaHealth.TransientUnverified;
        }
    }

    /// <summary>
    /// Attempts to resolve a catalog-bound CTA. Returns <c>true</c>/<c>false</c>
    /// for definitive availability; <c>null</c> when a transient error occurs
    /// (timeout / 5xx / OperationCanceledException). Caller decides fail-open
    /// vs. fail-closed.
    /// </summary>
    private async Task<bool?> TryResolveAsync(string ctaKind, Guid targetId, string marketCode, CancellationToken ct)
    {
        try
        {
            return ctaKind switch
            {
                "product" => (await _products.ReadAsync(targetId, marketCode, ct)).IsAvailable,
                "category" => (await _categories.ReadAsync(targetId, marketCode, ct)).IsAvailable,
                "bundle" => (await _bundles.ReadAsync(targetId, marketCode, ct)).IsAvailable,
                _ => null,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCatalogBound(string ctaKind) =>
        ctaKind is "product" or "category" or "bundle";
}

/// <summary>
/// Thrown by <see cref="BannerCtaValidator.ValidateForPublishAsync"/> when a
/// banner's catalog-bound CTA cannot be resolved at publish time.
/// </summary>
public sealed class BannerCtaTargetUnresolvableException(Guid bannerId, string ctaKind, string? ctaTarget)
    : Exception($"Banner {bannerId} CTA cannot be resolved: kind={ctaKind} target={ctaTarget ?? "<null>"}.")
{
    public Guid BannerId { get; } = bannerId;
    public string CtaKind { get; } = ctaKind;
    public string? CtaTarget { get; } = ctaTarget;
    public string ReasonCode => CmsReasonCode.BannerCtaTargetUnresolvable;
}
