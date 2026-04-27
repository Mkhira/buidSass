import 'package:get_it/get_it.dart';

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
import '../features/cart/data/cart_repository.dart';
import '../features/catalog/data/catalog_repository.dart';
import '../features/checkout/data/checkout_repository.dart';
import '../features/home/data/cms_stub_repository.dart';
import '../features/home/data/home_repository.dart';
import '../features/more/data/addresses_repository.dart';
import '../features/orders/data/orders_repository.dart';

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
      onRefreshStarted: () =>
          sl<AuthSessionBloc>().add(const RefreshStarted()),
      onRefreshSucceeded: (accessToken, refreshToken) =>
          sl<AuthSessionBloc>().add(RefreshSucceeded(
        accessToken: accessToken,
        refreshToken: refreshToken,
      )),
      onRefreshFailed: () =>
          sl<AuthSessionBloc>().add(const RefreshFailed()),
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
  sl.registerLazySingleton<AuthRepository>(StubAuthRepository.new);
  sl.registerLazySingleton<CheckoutRepository>(StubCheckoutRepository.new);
  sl.registerLazySingleton<OrdersRepository>(StubOrdersRepository.new);
  sl.registerLazySingleton<AddressesRepository>(StubAddressesRepository.new);

  sl.registerSingleton<bool>(true, instanceName: 'di.bootstrapped');
}

Future<void> resetDi() async {
  await sl.reset();
}
