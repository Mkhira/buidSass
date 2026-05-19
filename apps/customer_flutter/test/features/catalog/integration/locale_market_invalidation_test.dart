import 'dart:async';

import 'package:customer_flutter/features/catalog/data/catalog_gateway_impl.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _CountingInterceptor extends Interceptor {
  int callCount = 0;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler h) {
    callCount++;
    h.resolve(Response<Object?>(
      requestOptions: options,
      statusCode: 200,
      data: const [],
    ));
  }
}

void main() {
  test(
    'locale switch fires invalidation stream → cache eviction → next call refetches',
    () async {
      final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
      final stub = _CountingInterceptor();
      dio.interceptors.add(stub);

      final localeSignals = StreamController<void>.broadcast();

      var locale = 'en';
      final gateway = CatalogGatewayImpl(
        dio: dio,
        locale: () => locale,
        invalidationSignal: localeSignals.stream,
      );

      // First call — populates the cache.
      await gateway.listCategories(market: 'ksa');
      expect(stub.callCount, 1);

      // Hot read — within TTL, served from cache.
      await gateway.listCategories(market: 'ksa');
      expect(stub.callCount, 1);

      // Simulate locale switch: flip the locale provider, broadcast the
      // invalidation signal (DI wires this to LocaleBloc.stream).
      locale = 'ar';
      localeSignals.add(null);
      await Future<void>.delayed(Duration.zero);

      // Next call must hit the network — old cache was evicted.
      await gateway.listCategories(market: 'ksa');
      expect(stub.callCount, 2);

      await localeSignals.close();
      await gateway.dispose();
    },
  );

  test(
    'market switch alone (without invalidation signal) still differentiates cache keys',
    () async {
      final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
      final stub = _CountingInterceptor();
      dio.interceptors.add(stub);

      final gateway = CatalogGatewayImpl(dio: dio, locale: () => 'en');

      await gateway.listCategories(market: 'ksa');
      await gateway.listCategories(market: 'eg');

      // Even without the invalidation stream, the cache key embeds
      // market — two distinct fetches.
      expect(stub.callCount, 2);
    },
  );
}
