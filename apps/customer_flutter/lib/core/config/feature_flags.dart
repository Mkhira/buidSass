/// FeatureFlags — `--dart-define` driven toggles consumed by features.
/// Flags default off; flip to `true` when the depended-on spec ships.
class FeatureFlags {
  const FeatureFlags({
    this.verificationCtaShipped = false,
    this.cmsContentShipped = false,
    this.realIdentityClientShipped = false,
    this.realSearchClientShipped = false,
    this.realCheckoutClientShipped = false,
    this.realOrdersClientShipped = false,
    this.realReturnsClientShipped = false,
    this.realInvoicesClientShipped = false,
    this.realVerificationClientShipped = false,
    this.realReviewsClientShipped = false,
  });

  factory FeatureFlags.fromEnvironment() {
    const verification =
        bool.fromEnvironment('VERIFICATION_CTA_SHIPPED', defaultValue: false);
    const cms =
        bool.fromEnvironment('CMS_CONTENT_SHIPPED', defaultValue: false);
    const identity =
        bool.fromEnvironment('IDENTITY_CLIENT_SHIPPED', defaultValue: false);
    const search =
        bool.fromEnvironment('SEARCH_CLIENT_SHIPPED', defaultValue: false);
    const checkout =
        bool.fromEnvironment('CHECKOUT_CLIENT_SHIPPED', defaultValue: false);
    const orders =
        bool.fromEnvironment('ORDERS_CLIENT_SHIPPED', defaultValue: false);
    const returns =
        bool.fromEnvironment('RETURNS_CLIENT_SHIPPED', defaultValue: false);
    const invoices =
        bool.fromEnvironment('INVOICES_CLIENT_SHIPPED', defaultValue: false);
    const verificationClient = bool.fromEnvironment(
      'VERIFICATION_CLIENT_SHIPPED',
      defaultValue: false,
    );
    const reviews =
        bool.fromEnvironment('REVIEWS_CLIENT_SHIPPED', defaultValue: false);
    return const FeatureFlags(
      verificationCtaShipped: verification,
      cmsContentShipped: cms,
      realIdentityClientShipped: identity,
      realSearchClientShipped: search,
      realCheckoutClientShipped: checkout,
      realOrdersClientShipped: orders,
      realReturnsClientShipped: returns,
      realInvoicesClientShipped: invoices,
      realVerificationClientShipped: verificationClient,
      realReviewsClientShipped: reviews,
    );
  }

  /// Spec 020 — when `true`, the more-menu verification entry routes to the
  /// real flow instead of the placeholder body.
  final bool verificationCtaShipped;

  /// Spec 022 — when `true`, the home Bloc consumes the real CMS adapter; on
  /// `false` it consumes [CmsStubRepository].
  final bool cmsContentShipped;

  /// `specs/mobile/phase-1-auth-identity` — when `true`, DI wires
  /// `AuthRepositoryImpl` (real HTTP calls to spec 004 identity endpoints)
  /// in place of `StubAuthRepository`. Default `false` so test runs and
  /// pre-backend dev builds keep the stub behavior. Flip via
  /// `--dart-define=IDENTITY_CLIENT_SHIPPED=true`.
  final bool realIdentityClientShipped;

  /// `specs/mobile/phase-3-search` — when `true`, DI wires
  /// `SearchGatewayImpl` (real HTTP calls to spec 006 search endpoints)
  /// in place of [StubSearchGateway]. Default `false` so dev builds run
  /// against the deterministic stub until the backend search client is
  /// reachable. Flip via `--dart-define=SEARCH_CLIENT_SHIPPED=true`.
  final bool realSearchClientShipped;

  /// `specs/mobile/phase-4-cart-checkout` — when `true`, DI wires
  /// `CheckoutGatewayImpl` (real HTTP calls to spec 010 checkout
  /// endpoints) in place of [StubCheckoutGateway]. Default `false` so
  /// dev builds run against the deterministic stub until the backend
  /// checkout client is reachable. Flip via
  /// `--dart-define=CHECKOUT_CLIENT_SHIPPED=true`.
  final bool realCheckoutClientShipped;

  /// `specs/mobile/phase-5-orders` — when `true`, DI wires
  /// `OrdersGatewayImpl` in place of [StubOrdersGateway]. Default
  /// `false`. Flip via `--dart-define=ORDERS_CLIENT_SHIPPED=true`.
  final bool realOrdersClientShipped;

  /// `specs/mobile/phase-6-returns-invoices` — when `true`, DI wires
  /// `ReturnsGatewayImpl` (real HTTP calls to spec 013 endpoints) in
  /// place of `StubReturnsGateway`. Default `false` so dev builds run
  /// against the deterministic stub. Flip via
  /// `--dart-define=RETURNS_CLIENT_SHIPPED=true`.
  final bool realReturnsClientShipped;

  /// `specs/mobile/phase-6-returns-invoices` — when `true`, DI wires
  /// `InvoicesGatewayImpl` (real HTTP calls to spec 012 tax-invoice
  /// endpoints) in place of `StubInvoicesGateway`. Default `false`.
  /// Flip via `--dart-define=INVOICES_CLIENT_SHIPPED=true`.
  final bool realInvoicesClientShipped;

  /// `specs/mobile/phase-7-trust-compliance` — when `true`, DI wires
  /// `VerificationGatewayImpl` (real HTTP calls to spec 020 endpoints)
  /// in place of `StubVerificationGateway`. Default `false`. Flip via
  /// `--dart-define=VERIFICATION_CLIENT_SHIPPED=true`.
  final bool realVerificationClientShipped;

  /// `specs/mobile/phase-7-trust-compliance` — when `true`, DI wires
  /// `ReviewsCustomerGatewayImpl` (real HTTP calls to spec 022 endpoints)
  /// in place of `StubReviewsCustomerGateway`. Default `false`. Flip via
  /// `--dart-define=REVIEWS_CLIENT_SHIPPED=true`.
  final bool realReviewsClientShipped;
}
