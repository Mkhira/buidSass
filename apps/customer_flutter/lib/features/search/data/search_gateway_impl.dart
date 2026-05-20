import 'package:dio/dio.dart';

import '../../../core/error/error_mapper.dart';
import 'models/search_models.dart';
import 'search_gateway.dart';

/// Dio-backed [SearchGateway]. Search responses are intentionally **not**
/// cached on the client: facets/sort/pagination produce a unique
/// `(query, facets, sort, page)` shape per call, and the Meilisearch
/// backend already serves them in sub-200ms (ADR-005).
class SearchGatewayImpl implements SearchGateway {
  SearchGatewayImpl({required Dio dio, ErrorMapper? errorMapper})
      : _dio = dio,
        _errors = errorMapper ?? const ErrorMapper();

  static const _autocompletePath = '/v1/customer/search/autocomplete';
  static const _productsPath = '/v1/customer/search/products';
  static const _lookupPath = '/v1/customer/search/lookup';

  final Dio _dio;
  final ErrorMapper _errors;

  @override
  Future<AutocompleteResult> autocomplete(AutocompleteRequest request) async {
    try {
      final res = await _dio.post<Object?>(
        _autocompletePath,
        data: request.toJson(),
      );
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: _autocompletePath),
          type: DioExceptionType.badResponse,
          error: 'Malformed autocomplete payload',
        );
      }
      return AutocompleteResult.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<SearchProductsResult> searchProducts(
      SearchProductsRequest request) async {
    try {
      final res = await _dio.post<Object?>(
        _productsPath,
        data: request.toJson(),
      );
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: _productsPath),
          type: DioExceptionType.badResponse,
          error: 'Malformed search products payload',
        );
      }
      return SearchProductsResult.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }

  @override
  Future<LookupResult> lookup(LookupRequest request) async {
    try {
      final res = await _dio.post<Object?>(
        _lookupPath,
        data: request.toJson(),
      );
      final raw = res.data;
      if (raw is! Map) {
        throw DioException(
          requestOptions: RequestOptions(path: _lookupPath),
          type: DioExceptionType.badResponse,
          error: 'Malformed lookup payload',
        );
      }
      return LookupResult.fromJson(Map<String, Object?>.from(raw));
    } on DioException catch (e) {
      throw _errors.fromDio(e);
    }
  }
}

/// In-memory stub gateway for offline development and the early-Phase-3
/// UI smoke pass while the backend search module is not yet reachable.
/// Returns deterministic seed data; honors BR-2 (server-side Arabic
/// normalization) by treating any non-empty query as a hit.
class StubSearchGateway implements SearchGateway {
  const StubSearchGateway();

  @override
  Future<AutocompleteResult> autocomplete(AutocompleteRequest request) async {
    if (request.query.trim().isEmpty) {
      return const AutocompleteResult(suggestions: [], topMatches: []);
    }
    final q = request.query.trim();
    return AutocompleteResult(
      suggestions: [
        SearchSuggestion(label: q, kind: 'term'),
        SearchSuggestion(label: '$q gel', kind: 'term'),
        const SearchSuggestion(
            label: 'Dental Tools', kind: 'category', linkSlug: 'dental-tools'),
      ],
      topMatches: [
        SearchTopMatch(
          productId: 'stub-1',
          slug: 'stub-product-1',
          name: '$q — Sample Product',
          imageUrl: '',
          priceHint: const SearchPriceHint(amount: '120.00', currency: 'SAR'),
        ),
      ],
    );
  }

  @override
  Future<SearchProductsResult> searchProducts(
      SearchProductsRequest request) async {
    if (request.query.trim().isEmpty) {
      return const SearchProductsResult(
        items: [],
        page: 1,
        pageSize: 24,
        totalCount: 0,
        facets: [],
        sortOptions: [],
      );
    }
    final q = request.query.trim();
    final items = List.generate(
      5,
      (i) => SearchProductItem(
        id: 'stub-$i',
        slug: 'stub-$i',
        name: '$q result $i',
        thumbnailUrl: '',
        priceMinor: 12000 + i * 100,
        currency: 'SAR',
        isRestricted: i == 0,
        inStock: i != 2,
      ),
    );
    return SearchProductsResult(
      items: items,
      page: request.page,
      pageSize: request.pageSize,
      totalCount: items.length,
      facets: const [
        SearchFacet(
          key: 'brand',
          label: 'Brand',
          type: 'checkbox',
          options: [
            SearchFacetOption(value: 'brand-x', label: 'Brand X', count: 12),
            SearchFacetOption(value: 'brand-y', label: 'Brand Y', count: 7),
          ],
        ),
      ],
      sortOptions: const [
        SearchSortOption(key: 'relevance', label: 'Relevance'),
        SearchSortOption(key: 'priceAsc', label: 'Price low to high'),
        SearchSortOption(key: 'priceDesc', label: 'Price high to low'),
      ],
    );
  }

  @override
  Future<LookupResult> lookup(LookupRequest request) async {
    final value = request.sku ?? request.barcode ?? '';
    if (value.trim().isEmpty) {
      return const LookupResult(matched: false);
    }
    // Stub: treat "NOTFOUND" prefix as no-match for testing the empty path.
    if (value.toUpperCase().startsWith('NOTFOUND')) {
      return const LookupResult(matched: false);
    }
    return LookupResult(
      matched: true,
      match: LookupMatch(
        productId: 'stub-${value.hashCode.abs()}',
        slug: 'stub-$value',
        name: 'Stub product for $value',
        kind: request.sku != null ? 'sku' : 'barcode',
      ),
    );
  }
}
