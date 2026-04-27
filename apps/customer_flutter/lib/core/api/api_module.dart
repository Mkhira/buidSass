import 'package:dio/dio.dart';

import '../auth/secure_token_store.dart';
import 'auth_interceptor.dart';
import 'correlation_id_interceptor.dart';
import 'dio_factory.dart';
import 'idempotency_interceptor.dart';
import 'locale_market_interceptor.dart';

/// ApiModule — assembles the canonical [Dio] instance with the four-stage
/// interceptor stack documented in research §R3 + plan.md.
///
/// Order matters:
/// 1. [LocaleMarketInterceptor] — every request carries the active locale
///    + market headers.
/// 2. [CorrelationIdInterceptor] — RFC-4122 v4 UUID per request.
/// 3. [IdempotencyInterceptor] — checkout submit only (reads
///    `extra['idempotencyKey']`).
/// 4. [AuthInterceptor] — attaches the Bearer access token; refreshes once
///    on 401.
class ApiModule {
  ApiModule({
    required this.dioFactory,
    required this.tokenStore,
    required this.locale,
    required this.market,
    required this.refresh,
  });

  final DioFactory dioFactory;
  final SecureTokenStore tokenStore;
  final LocaleProvider locale;
  final MarketProvider market;
  final RefreshTokenFn refresh;

  late final Dio dio = _buildDio();

  Dio _buildDio() {
    final dio = dioFactory.create();
    dio.interceptors.addAll([
      LocaleMarketInterceptor(locale: locale, market: market),
      CorrelationIdInterceptor(),
      const IdempotencyInterceptor(),
      AuthInterceptor(tokenStore: tokenStore, refresh: refresh, dio: dio),
    ]);
    return dio;
  }
}
