import 'package:get_it/get_it.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/api/api_module.dart';
import '../core/api/auth_interceptor.dart';
import '../core/api/dio_factory.dart';
import '../core/auth/auth_session_bloc.dart';
import '../core/auth/secure_token_store.dart';
import '../core/cart/anonymous_cart_token_store.dart';
import '../core/config/feature_flags.dart';
import '../core/localization/locale_bloc.dart';
import '../core/market/market_resolver.dart';
import '../core/observability/telemetry_adapter.dart';
import '../core/platform/app_links_adapter.dart';
import '../core/platform/secure_storage_web.dart';
import '../core/platform/sms_autofill_adapter.dart';
import '../features/auth/data/auth_repository.dart';
import '../features/auth/data/auth_repository_impl.dart';
import '../features/cart/data/cart_repository.dart';
import '../features/cart/data/cart_store.dart';
import '../features/catalog/data/catalog_repository.dart';
import '../features/checkout/data/checkout_gateway.dart';
import '../features/checkout/data/checkout_gateway_impl.dart';
import '../features/checkout/data/checkout_repository.dart';
import '../features/checkout/data/session_store.dart';
import '../features/checkout/data/stub_checkout_gateway.dart';
import '../features/home/data/cms_stub_repository.dart';
import '../features/home/data/home_repository.dart';
import '../features/invoices/data/invoices_gateway.dart';
import '../features/invoices/data/invoices_gateway_impl.dart';
import '../features/invoices/data/stub_invoices_gateway.dart';
import '../features/more/data/addresses_repository.dart';
import '../features/orders/data/orders_gateway.dart';
import '../features/orders/data/orders_gateway_impl.dart';
import '../features/orders/data/orders_repository.dart';
import '../features/orders/data/stub_orders_gateway.dart';
import '../features/returns/data/returns_gateway.dart';
import '../features/returns/data/returns_gateway_impl.dart';
import '../features/returns/data/stub_returns_gateway.dart';
import '../features/reviews/data/reviews_customer_gateway.dart';
import '../features/reviews/data/reviews_customer_gateway_impl.dart';
import '../features/reviews/data/stub_reviews_customer_gateway.dart';
import '../features/search/data/recent_searches_store.dart';
import '../features/search/data/search_gateway.dart';
import '../features/search/data/search_gateway_impl.dart';
import '../features/verification/data/stub_verification_gateway.dart';
import '../features/verification/data/verification_gateway.dart';
import '../features/verification/data/verification_gateway_impl.dart';

/// GetIt composition root. Boots in [bootstrap]; feature modules and tests
/// register additional bindings on top via [GetIt.I].
final GetIt sl = GetIt.instance;

Future<void> bootstrap({
  TelemetryAdapter? telemetryOverride,
  SecureTokenStore? tokenStoreOverride,
}) async {
  if (sl.isRegistered<bool>(instanceName: 'di.bootstrapped')) return;

  // Configuration
  sl.registerSingleton<FeatureFlags>(FeatureFlags.fromEnvironment());
  sl.registerLazySingleton<SecureStoragePlatformOptions>(
    () => const SecureStoragePlatformOptions(),
  );

  // Observability
  sl.registerSingleton<TelemetryAdapter>(
    telemetryOverride ?? const NoopTelemetryAdapter(),
  );

  // Auth + storage
  sl.registerSingleton<SecureTokenStore>(
    tokenStoreOverride ??
        SecureTokenStore(
          storage: sl<SecureStoragePlatformOptions>().build(),
          telemetry: sl<TelemetryAdapter>(),
        ),
  );
  sl.registerLazySingleton<AnonymousCartTokenStore>(
    () => AnonymousCartTokenStore(
      storage: sl<SecureStoragePlatformOptions>().build(),
    ),
  );

  // Locale + market
  sl.registerSingleton<LocaleBloc>(LocaleBloc());
  sl.registerSingleton<MarketResolver>(MarketResolver());

  // Auth Bloc — depends on token store + telemetry
  sl.registerSingleton<AuthSessionBloc>(
    AuthSessionBloc(
      tokenStore: sl<SecureTokenStore>(),
      telemetry: sl<TelemetryAdapter>(),
    ),
  );

  // API stack
  sl.registerSingleton<DioFactory>(
    DioFactory(DioFactoryConfig.fromEnvironment()),
  );
  sl.registerSingleton<ApiModule>(
    ApiModule(
      dioFactory: sl<DioFactory>(),
      tokenStore: sl<SecureTokenStore>(),
      locale: () => sl<LocaleBloc>().state.locale.code,
      market: () => sl<MarketResolver>().resolve().code,
      // Refresh stub — wired to spec 004 client when generated. Today it
      // signals failure so the AuthSessionBloc transitions to RefreshFailed.
      refresh: (_) async => const RefreshOutcome.failure(),
      // Lifecycle hooks bridge the HTTP refresh-and-retry path with SM-1.
      // When the HTTP layer detects a stale token and refreshes, we keep
      // the Bloc state aligned so the router redirect re-evaluates and
      // unauthenticated users land on /auth/login.
      onRefreshStarted: () => sl<AuthSessionBloc>().add(const RefreshStarted()),
      onRefreshSucceeded: (accessToken, refreshToken) =>
          sl<AuthSessionBloc>().add(RefreshSucceeded(
        accessToken: accessToken,
        refreshToken: refreshToken,
      )),
      onRefreshFailed: () => sl<AuthSessionBloc>().add(const RefreshFailed()),
    ),
  );

  // Platform adapters
  sl.registerLazySingleton<SmsAutofillAdapter>(SmsAutofillAdapter.new);
  sl.registerLazySingleton<AppLinksAdapter>(AppLinksAdapter.new);

  // Feature repositories — stub adapters until generated clients land.
  sl.registerLazySingleton<CmsRepository>(() => const CmsStubRepository());
  sl.registerLazySingleton<HomeRepository>(
    () => DefaultHomeRepository(cms: sl<CmsRepository>()),
  );
  sl.registerLazySingleton<CatalogRepository>(StubCatalogRepository.new);
  sl.registerLazySingleton<CartRepository>(StubCartRepository.new);
  sl.registerLazySingleton<AuthRepository>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realIdentityClientShipped) {
      return AuthRepositoryImpl(dio: sl<ApiModule>().dio);
    }
    return StubAuthRepository();
  });
  sl.registerLazySingleton<CheckoutRepository>(StubCheckoutRepository.new);
  sl.registerLazySingleton<OrdersRepository>(StubOrdersRepository.new);
  sl.registerLazySingleton<AddressesRepository>(StubAddressesRepository.new);

  // Search — Phase 3. Backend search module is not yet wired into the
  // app's Dio stack; until the OpenAPI client lands the stub gateway
  // satisfies BR-1..7 against deterministic seed data.
  sl.registerLazySingleton<SearchGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realSearchClientShipped) {
      return SearchGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return const StubSearchGateway();
  });
  final prefs = await SharedPreferences.getInstance();
  sl.registerLazySingleton<RecentSearchesStore>(
    () => SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () {
        final s = sl<AuthSessionBloc>().state;
        if (s is AuthAuthenticated) return s.customerId;
        return null;
      },
    ),
  );

  // Cart + checkout — Phase 4. CartStore is a singleton so every bloc
  // and the order-confirmation screen reads/writes the same persisted
  // snapshot. CheckoutGateway flips between Dio impl and stub via
  // `CHECKOUT_CLIENT_SHIPPED` dart-define (default stub for offline dev).
  final cartStore = CartStore(prefs: prefs);
  await cartStore.load();
  sl.registerSingleton<CartStore>(cartStore);
  sl.registerLazySingleton<CheckoutSessionStore>(
    () => CheckoutSessionStore(prefs: prefs),
  );
  sl.registerLazySingleton<CheckoutGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realCheckoutClientShipped) {
      return CheckoutGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubCheckoutGateway();
  });
  sl.registerLazySingleton<OrdersGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realOrdersClientShipped) {
      return OrdersGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubOrdersGateway();
  });

  // Phase 6 — returns + invoices. Gateways flip between Dio impls and
  // deterministic stubs via dart-defines so dev builds run offline
  // until the backend clients land. The same `ApiModule.dio` carries
  // the auth + correlation-id + idempotency interceptors; returns'
  // create + photo-upload routes use the existing
  // `IdempotencyInterceptor` extras pattern (see
  // returns_gateway_impl.dart).
  sl.registerLazySingleton<ReturnsGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realReturnsClientShipped) {
      return ReturnsGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubReturnsGateway();
  });
  sl.registerLazySingleton<InvoicesGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realInvoicesClientShipped) {
      return InvoicesGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubInvoicesGateway();
  });

  // Phase 7 — verification + reviews. Gateways flip between Dio impls
  // and deterministic stubs via dart-defines so dev builds run offline
  // until the backend clients land. Idempotency on submit/resubmit/
  // renew + review submit routes through the existing
  // `IdempotencyInterceptor` extras pattern.
  sl.registerLazySingleton<VerificationGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realVerificationClientShipped) {
      return VerificationGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubVerificationGateway();
  });
  sl.registerLazySingleton<ReviewsCustomerGateway>(() {
    final flags = sl<FeatureFlags>();
    if (flags.realReviewsClientShipped) {
      return ReviewsCustomerGatewayImpl(dio: sl<ApiModule>().dio);
    }
    return StubReviewsCustomerGateway();
  });

  // Clear cart on sign-out (BR-1). The subscription survives DI bootstrap
  // for the lifetime of the app, so we don't track the StreamSubscription
  // — `sl.reset()` in tests tears down via the AuthSessionBloc close().
  var wasAuth = sl<AuthSessionBloc>().state is AuthAuthenticated;
  sl<AuthSessionBloc>().stream.listen((s) async {
    final isAuth = s is AuthAuthenticated;
    if (wasAuth && !isAuth) {
      await cartStore.clear();
    }
    wasAuth = isAuth;
  });

  sl.registerSingleton<bool>(true, instanceName: 'di.bootstrapped');
}

Future<void> resetDi() async {
  await sl.reset();
}
