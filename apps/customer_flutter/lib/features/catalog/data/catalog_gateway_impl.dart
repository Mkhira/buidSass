import 'dart:async';

import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'catalog_gateway.dart';
import 'models/catalog_models.dart';

/// Pluggable clock for cache-TTL tests (data-model.md §Local cache schema).
typedef CatalogClock = DateTime Function();

/// Dio-backed [CatalogGateway] with an in-memory TTL cache keyed by
/// `cat:{locale}:{market}:{endpoint}:{queryHash}`. The cache is cleared
/// whenever the [invalidationSignal] stream emits — DI wires that to
/// LocaleBloc + MarketResolver changes so cached AR responses don't bleed
/// into an EN session (BR-5).
class CatalogGatewayImpl implements CatalogGateway {
  CatalogGatewayImpl({
    required Dio dio,
    required String Function() locale,
    ErrorMapper? errorMapper,
    CatalogClock? clock,
    Duration ttl = const Duration(minutes: 5),
    Stream<void>? invalidationSignal,
  })  : _dio = dio,
        _locale = locale,
        _errors = errorMapper ?? const ErrorMapper(),
        _clock = clock ?? DateTime.now,
        _ttl = ttl {
    _invalidationSub = invalidationSignal?.listen((_) => clearCache());
  }

  static const _categoriesPath = '/v1/customer/catalog/categories';
  static const _brandsPath = '/v1/customer/catalog/brands';

  final Dio _dio;
  final String Function() _locale;
  final ErrorMapper _errors;
  final CatalogClock _clock;
  final Duration _ttl;
  final Map<String, _CacheEntry> _cache = {};
  StreamSubscription<void>? _invalidationSub;

  Future<void> dispose() async {
    await _invalidationSub?.cancel();
    _invalidationSub = null;
  }

  @override
  Future<List<CatalogCategory>> listCategories({required String market}) {
    final key = _key(market, 'categories');
    return _readThrough<List<CatalogCategory>>(key, () async {
      final res = await _dio.get<Object?>(
        _categoriesPath,
        queryParameters: {'market': market},
      );
      return _decodeList(res.data, CatalogCategory.fromJson,
          path: _categoriesPath);
    });
  }

  @override
  Future<List<CatalogBrand>> listBrands({required String market}) {
    final key = _key(market, 'brands');
    return _readThrough<List<CatalogBrand>>(key, () async {
      final res = await _dio.get<Object?>(
        _brandsPath,
        queryParameters: {'market': market},
      );
      return _decodeList(res.data, CatalogBrand.fromJson, path: _brandsPath);
    });
  }

  @override
  Future<CatalogProductPage> listCategoryProducts({
    required String slug,
    required String market,
    int page = 1,
    int pageSize = 20,
    CatalogSort? sort,
    String? brand,
    int? priceMin,
    int? priceMax,
    CatalogRestrictedFilter? restricted,
  }) {
    final query = <String, Object?>{
      'market': market,
      'page': page,
      'pageSize': pageSize,
      if (sort != null) 'sort': sort.wire,
      if (brand != null && brand.isNotEmpty) 'brand': brand,
      if (priceMin != null) 'priceMin': priceMin,
      if (priceMax != null) 'priceMax': priceMax,
      if (restricted != null) 'restricted': restricted.wire,
    };
    final key = _key(market, 'cat/$slug/products:${_hashQuery(query)}');
    return _readThrough<CatalogProductPage>(key, () async {
      final res = await _dio.get<Object?>(
        '/v1/customer/catalog/categories/$slug/products',
        queryParameters: query,
      );
      final raw = res.data;
      if (raw is Map) {
        return CatalogProductPage.fromJson(Map<String, Object?>.from(raw));
      }
      if (raw is List) {
        // Some server builds return the array directly when paging is off.
        return CatalogProductPage.fromJson({'items': raw});
      }
      // Malformed payload — surface as a typed failure rather than a
      // silent empty page, so the bloc can render an error state and
      // we don't cache a fake-empty response.
      throw DioException(
        requestOptions: RequestOptions(
          path: '/v1/customer/catalog/categories/$slug/products',
        ),
        type: DioExceptionType.badResponse,
        error: 'Malformed category products payload',
      );
    });
  }

  @override
  Future<CatalogProductDetail> getProductBySlug({
    required String slug,
    required String market,
  }) {
    final key = _key(market, 'product/$slug');
    return _readThrough<CatalogProductDetail>(key, () async {
      final res = await _dio.get<Object?>(
        '/v1/customer/catalog/products/$slug',
        queryParameters: {'market': market},
      );
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: '/v1/customer/catalog/products/$slug'),
          type: DioExceptionType.unknown,
          error: 'Malformed product detail payload',
        );
      }
      return CatalogProductDetail.fromJson(Map<String, Object?>.from(raw));
    });
  }

  @override
  void clearCache() => _cache.clear();

  // ----- helpers -----

  String _key(String market, String suffix) =>
      'cat:${_locale()}:$market:$suffix';

  Future<T> _readThrough<T>(String key, Future<T> Function() loader) async {
    final hit = _cache[key];
    if (hit != null && !hit.isExpired(_clock(), _ttl)) {
      return hit.value as T;
    }
    try {
      final value = await loader();
      _cache[key] = _CacheEntry(value: value, storedAt: _clock());
      return value;
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  List<T> _decodeList<T>(
    Object? data,
    T Function(Map<String, Object?>) fromJson, {
    required String path,
  }) {
    if (data is! List) {
      // Surface a typed failure rather than a silent empty list — the
      // _readThrough wrapper will not cache the result, and the bloc
      // gets the same error path as any other server fault.
      throw DioException(
        requestOptions: RequestOptions(path: path),
        type: DioExceptionType.badResponse,
        error: 'Malformed list payload',
      );
    }
    return data
        .whereType<Map>()
        .map((m) => fromJson(Map<String, Object?>.from(m)))
        .toList(growable: false);
  }

  String _hashQuery(Map<String, Object?> query) {
    final entries = query.entries.toList()
      ..sort((a, b) => a.key.compareTo(b.key));
    return entries.map((e) => '${e.key}=${e.value}').join('&');
  }
}

class _CacheEntry {
  const _CacheEntry({required this.value, required this.storedAt});

  final Object? value;
  final DateTime storedAt;

  bool isExpired(DateTime now, Duration ttl) =>
      now.difference(storedAt) >= ttl;
}
