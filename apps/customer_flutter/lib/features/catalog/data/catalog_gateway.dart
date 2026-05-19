import 'models/catalog_models.dart';

/// Read surface for the customer catalog. Backs Phase 2 screens
/// (S-2.1 Home, S-2.2 Categories, S-2.3 Category detail, S-2.4 Brands,
/// S-2.5 Product list, S-2.6 PDP) and is reused by Phase 4 add-to-cart
/// product lookups.
///
/// All methods throw a typed [Failure] (from `core/error/failure.dart`) on
/// transport / HTTP error — callers convert to their bloc-state shapes.
///
/// Endpoints (per `services/backend_api/openapi.catalog.json`):
///
///   * GET `/v1/customer/catalog/categories`              → [listCategories]
///   * GET `/v1/customer/catalog/brands`                  → [listBrands]
///   * GET `/v1/customer/catalog/categories/{slug}/products`
///                                                       → [listCategoryProducts]
///   * GET `/v1/customer/catalog/products/{slug}`         → [getProductBySlug]
abstract class CatalogGateway {
  Future<List<CatalogCategory>> listCategories({required String market});

  Future<List<CatalogBrand>> listBrands({required String market});

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
  });

  Future<CatalogProductDetail> getProductBySlug({
    required String slug,
    required String market,
  });

  /// Drop every cached entry. Called by tests and by the locale/market
  /// invalidation signal wired in DI (Phase 2 BR-5).
  void clearCache();
}
