import 'models/search_models.dart';

/// Customer search surface — the 3 endpoints under `/v1/customer/search/`
/// per `services/backend_api/openapi.search.json`. All methods throw a
/// typed [Failure] (from `core/error/failure.dart`) on transport / HTTP
/// error; callers translate into bloc-state shapes.
///
/// Arabic normalization is server-side (BR-2) — callers send the raw
/// query string and let Meilisearch (ADR-005) do the folding.
abstract class SearchGateway {
  Future<AutocompleteResult> autocomplete(AutocompleteRequest request);

  Future<SearchProductsResult> searchProducts(SearchProductsRequest request);

  Future<LookupResult> lookup(LookupRequest request);
}
