import 'dart:developer' as developer;

/// TelemetryAdapter — research §R10. The customer app emits a closed set of
/// events (see `contracts/client-events.md`). v1 ships [NoopTelemetryAdapter];
/// dev / debug builds may swap in [ConsoleTelemetryAdapter]. A real provider
/// (Mixpanel / Amplitude / App Insights) is one composition-root swap away.
abstract class TelemetryAdapter {
  const TelemetryAdapter();

  void emit(String event, {Map<String, Object?> properties = const {}});
}

class NoopTelemetryAdapter extends TelemetryAdapter {
  const NoopTelemetryAdapter();

  @override
  void emit(String event, {Map<String, Object?> properties = const {}}) {}
}

class ConsoleTelemetryAdapter extends TelemetryAdapter {
  const ConsoleTelemetryAdapter();

  @override
  void emit(String event, {Map<String, Object?> properties = const {}}) {
    developer.log('telemetry.$event ${properties.toString()}',
        name: 'telemetry');
  }
}

/// Closed allow-list for events from `contracts/client-events.md`. The
/// PII-guard test (T034) reads this set and asserts every emit site is a
/// member; properties are also bounded to the per-event allow-list below.
const Set<String> kAllowedTelemetryEvents = {
  'app.cold_start',
  'app.foregrounded',
  'auth.register.started',
  'auth.register.success',
  'auth.register.failure',
  'auth.login.started',
  'auth.login.success',
  'auth.login.failure',
  'auth.otp.requested',
  'auth.otp.resent',
  'auth.otp.success',
  'auth.otp.failure',
  'auth.password.reset.requested',
  'auth.password.reset.completed',
  'auth.storage.migrated',
  'language.toggled',
  'home.opened',
  'home.first_contentful_paint_ms',
  'listing.opened',
  'listing.facet.applied',
  'listing.sort.changed',
  'detail.opened',
  'cart.add',
  'cart.opened',
  'cart.line.removed',
  'cart.line.qty.changed',
  'cart.out_of_sync.detected',
  'checkout.started',
  'checkout.address.selected',
  'checkout.shipping.selected',
  'checkout.payment.selected',
  'checkout.submit.tapped',
  'checkout.submit.success',
  'checkout.submit.drift',
  'checkout.submit.failure',
  'orders.list.opened',
  'order.detail.opened',
  'order.reorder.tapped',
  'order.support.tapped',
  'more.address.added',
  'more.address.edited',
  'more.verification.cta.tapped',
  'more.logout.tapped',
};

/// Per-event allow-listed property keys. The PII guard fails if an emit
/// site carries a key not declared here.
const Map<String, Set<String>> kAllowedTelemetryProps = {
  'app.cold_start': {'platform', 'locale', 'market', 'cold_start_ms'},
  'app.foregrounded': {'bg_duration_ms'},
  'auth.register.started': {'entry_point'},
  'auth.register.success': {},
  'auth.register.failure': {'reason_code'},
  'auth.login.started': {'entry_point', 'continue_to_present'},
  'auth.login.success': {},
  'auth.login.failure': {'reason_code'},
  'auth.otp.requested': {'channel'},
  'auth.otp.resent': {'channel'},
  'auth.otp.success': {},
  'auth.otp.failure': {'reason_code'},
  'auth.password.reset.requested': {},
  'auth.password.reset.completed': {},
  'auth.storage.migrated': {'from_version', 'to_version', 'outcome'},
  'language.toggled': {'from', 'to'},
  'home.opened': {'time_to_interactive_ms'},
  'home.first_contentful_paint_ms': {'value_ms', 'platform'},
  'listing.opened': {'category_id', 'query_present'},
  'listing.facet.applied': {'facet_kind'},
  'listing.sort.changed': {'sort_key'},
  'detail.opened': {'product_id', 'is_restricted'},
  'cart.add': {'product_id', 'qty', 'is_restricted_and_unverified'},
  'cart.opened': {'revision', 'line_count'},
  'cart.line.removed': {'product_id'},
  'cart.line.qty.changed': {'product_id', 'delta'},
  'cart.out_of_sync.detected': {},
  'checkout.started': {},
  'checkout.address.selected': {},
  'checkout.shipping.selected': {},
  'checkout.payment.selected': {'method_kind'},
  'checkout.submit.tapped': {},
  'checkout.submit.success': {'order_id', 'payment_state', 'fulfillment_state'},
  'checkout.submit.drift': {},
  'checkout.submit.failure': {'reason_code'},
  'orders.list.opened': {},
  'order.detail.opened': {'order_id'},
  'order.reorder.tapped': {'order_id', 'out_of_stock_count'},
  'order.support.tapped': {'order_id'},
  'more.address.added': {},
  'more.address.edited': {},
  'more.verification.cta.tapped': {'is_placeholder'},
  'more.logout.tapped': {},
};
