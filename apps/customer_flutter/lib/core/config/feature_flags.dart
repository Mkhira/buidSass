/// FeatureFlags — `--dart-define` driven toggles consumed by features.
/// Flags default off; flip to `true` when the depended-on spec ships.
class FeatureFlags {
  const FeatureFlags({
    this.verificationCtaShipped = false,
    this.cmsContentShipped = false,
  });

  factory FeatureFlags.fromEnvironment() {
    const verification =
        bool.fromEnvironment('VERIFICATION_CTA_SHIPPED', defaultValue: false);
    const cms =
        bool.fromEnvironment('CMS_CONTENT_SHIPPED', defaultValue: false);
    return const FeatureFlags(
      verificationCtaShipped: verification,
      cmsContentShipped: cms,
    );
  }

  /// Spec 020 — when `true`, the more-menu verification entry routes to the
  /// real flow instead of the placeholder body.
  final bool verificationCtaShipped;

  /// Spec 022 — when `true`, the home Bloc consumes the real CMS adapter; on
  /// `false` it consumes [CmsStubRepository].
  final bool cmsContentShipped;
}
