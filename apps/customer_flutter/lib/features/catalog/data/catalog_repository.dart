import '../../../core/api/i18n_aware_repository.dart';
import 'catalog_view_models.dart';

abstract class CatalogRepository {
  Future<ListingPage> fetchListing({
    String? query,
    String? categoryId,
    String? sortKey,
    Map<String, Set<String>> selectedFacets = const {},
    String? cursor,
  });

  Future<ProductDetailViewModel> fetchDetail(String productId);
}

/// Stub adapter — emits an empty listing + a 404-shaped detail until spec
/// 005 / 006 OpenAPI clients are generated. Repository contract remains
/// stable so the listing/detail Blocs can be developed and tested today.
class StubCatalogRepository
    with I18nAwareRepository
    implements CatalogRepository {
  @override
  Future<ListingPage> fetchListing({
    String? query,
    String? categoryId,
    String? sortKey,
    Map<String, Set<String>> selectedFacets = const {},
    String? cursor,
  }) async {
    return const ListingPage(items: [], facets: [], nextCursor: null);
  }

  @override
  Future<ProductDetailViewModel> fetchDetail(String productId) async {
    throw const CatalogGapException();
  }
}

class CatalogGapException implements Exception {
  const CatalogGapException();
  @override
  String toString() =>
      'Catalog client gap — escalate to spec 005/006 (FR-031).';
}
