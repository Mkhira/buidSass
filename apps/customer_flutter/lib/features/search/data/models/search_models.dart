import 'package:flutter/foundation.dart';

/// `POST /v1/customer/search/autocomplete` request payload.
@immutable
class AutocompleteRequest {
  const AutocompleteRequest({
    required this.query,
    required this.marketCode,
    required this.locale,
    this.topMatchesLimit = 5,
  });

  final String query;
  final String marketCode;
  final String locale;
  final int topMatchesLimit;

  Map<String, Object?> toJson() => {
        'query': query,
        'marketCode': marketCode,
        'locale': locale,
        'topMatchesLimit': topMatchesLimit,
      };
}

/// Single autocomplete suggestion entry — terms, categories, or brands.
@immutable
class SearchSuggestion {
  const SearchSuggestion({
    required this.label,
    required this.kind,
    this.linkSlug,
  });

  final String label;
  final String kind; // term | category | brand
  final String? linkSlug;

  factory SearchSuggestion.fromJson(Map<String, Object?> j) => SearchSuggestion(
        label: j['label'] as String? ?? '',
        kind: j['kind'] as String? ?? 'term',
        linkSlug: j['linkSlug'] as String?,
      );
}

/// Top-match product card strip element rendered above the suggestions.
@immutable
class SearchTopMatch {
  const SearchTopMatch({
    required this.productId,
    required this.slug,
    required this.name,
    required this.imageUrl,
    required this.priceHint,
  });

  final String productId;
  final String slug;
  final String name;
  final String imageUrl;
  final SearchPriceHint priceHint;

  factory SearchTopMatch.fromJson(Map<String, Object?> j) {
    final priceRaw = j['priceHint'];
    return SearchTopMatch(
      productId: j['productId'] as String? ?? '',
      slug: j['slug'] as String? ?? '',
      name: j['name'] as String? ?? '',
      imageUrl: j['imageUrl'] as String? ?? '',
      priceHint: priceRaw is Map
          ? SearchPriceHint.fromJson(Map<String, Object?>.from(priceRaw))
          : const SearchPriceHint(amount: '', currency: ''),
    );
  }
}

@immutable
class SearchPriceHint {
  const SearchPriceHint({required this.amount, required this.currency});
  final String amount;
  final String currency;

  factory SearchPriceHint.fromJson(Map<String, Object?> j) => SearchPriceHint(
        amount: j['amount'] as String? ?? '',
        currency: j['currency'] as String? ?? '',
      );
}

@immutable
class AutocompleteResult {
  const AutocompleteResult({
    required this.suggestions,
    required this.topMatches,
  });

  final List<SearchSuggestion> suggestions;
  final List<SearchTopMatch> topMatches;

  factory AutocompleteResult.fromJson(Map<String, Object?> j) {
    final s = j['suggestions'];
    final t = j['topMatches'];
    return AutocompleteResult(
      suggestions: s is List
          ? s
              .whereType<Map>()
              .map((m) =>
                  SearchSuggestion.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      topMatches: t is List
          ? t
              .whereType<Map>()
              .map((m) => SearchTopMatch.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

/// `POST /v1/customer/search/products` request payload.
@immutable
class SearchProductsRequest {
  const SearchProductsRequest({
    required this.query,
    required this.marketCode,
    required this.locale,
    this.page = 1,
    this.pageSize = 24,
    this.sort,
    this.facets = const {},
  });

  final String query;
  final String marketCode;
  final String locale;
  final int page;
  final int pageSize;
  final String? sort;
  final Map<String, Object?> facets;

  Map<String, Object?> toJson() => {
        'query': query,
        'marketCode': marketCode,
        'locale': locale,
        'page': page,
        'pageSize': pageSize,
        if (sort != null) 'sort': sort,
        if (facets.isNotEmpty) 'facets': facets,
      };
}

/// Product card item in the search results grid. Same shape as the Phase 2
/// catalog product list item so the shared `ProductCard` widget can render
/// both without an adapter layer (BR-4).
@immutable
class SearchProductItem {
  const SearchProductItem({
    required this.id,
    required this.slug,
    required this.name,
    required this.thumbnailUrl,
    required this.priceMinor,
    required this.currency,
    required this.isRestricted,
    required this.inStock,
  });

  final String id;
  final String slug;
  final String name;
  final String thumbnailUrl;
  final int priceMinor;
  final String currency;
  final bool isRestricted;
  final bool inStock;

  factory SearchProductItem.fromJson(Map<String, Object?> j) {
    int parsePriceMinor(Object? v) {
      if (v is int) return v;
      if (v is num) return v.toInt();
      if (v is String) return int.tryParse(v) ?? 0;
      return 0;
    }

    bool parseBool(Object? v, {bool fallback = false}) {
      if (v is bool) return v;
      if (v is String) return v.toLowerCase() == 'true';
      return fallback;
    }

    return SearchProductItem(
      id: j['id'] as String? ?? j['productId'] as String? ?? '',
      slug: j['slug'] as String? ?? '',
      name: j['name'] as String? ?? '',
      thumbnailUrl:
          j['thumbnailUrl'] as String? ?? j['imageUrl'] as String? ?? '',
      priceMinor: parsePriceMinor(j['priceMinor'] ?? j['price']),
      currency: j['currency'] as String? ?? '',
      isRestricted: parseBool(j['isRestricted'] ?? j['restricted']),
      inStock: parseBool(j['inStock'], fallback: true),
    );
  }
}

@immutable
class SearchFacetOption {
  const SearchFacetOption({
    required this.value,
    required this.label,
    required this.count,
  });

  final String value;
  final String label;
  final int count;

  factory SearchFacetOption.fromJson(Map<String, Object?> j) =>
      SearchFacetOption(
        value: j['value'] as String? ?? '',
        label: j['label'] as String? ?? '',
        count: (j['count'] as num?)?.toInt() ?? 0,
      );
}

@immutable
class SearchFacet {
  const SearchFacet({
    required this.key,
    required this.label,
    required this.type,
    required this.options,
  });

  final String key;
  final String label;
  final String type; // checkbox | range | radio
  final List<SearchFacetOption> options;

  factory SearchFacet.fromJson(Map<String, Object?> j) {
    final opts = j['options'];
    return SearchFacet(
      key: j['key'] as String? ?? '',
      label: j['label'] as String? ?? '',
      type: j['type'] as String? ?? 'checkbox',
      options: opts is List
          ? opts
              .whereType<Map>()
              .map((m) =>
                  SearchFacetOption.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

@immutable
class SearchSortOption {
  const SearchSortOption({required this.key, required this.label});
  final String key;
  final String label;

  factory SearchSortOption.fromJson(Map<String, Object?> j) => SearchSortOption(
        key: j['key'] as String? ?? '',
        label: j['label'] as String? ?? '',
      );
}

@immutable
class SearchProductsResult {
  const SearchProductsResult({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.facets,
    required this.sortOptions,
    this.suggestions = const [],
  });

  final List<SearchProductItem> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final List<SearchFacet> facets;
  final List<SearchSortOption> sortOptions;

  /// "Did you mean" suggestions returned with zero-result responses
  /// (spec.md §S-3.3 edge cases).
  final List<String> suggestions;

  bool get hasMore => page * pageSize < totalCount;

  factory SearchProductsResult.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    final facets = j['facets'];
    final sortOpts = j['sortOptions'];
    final sugg = j['suggestions'];
    return SearchProductsResult(
      items: items is List
          ? items
              .whereType<Map>()
              .map((m) =>
                  SearchProductItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      page: (j['page'] as num?)?.toInt() ?? 1,
      pageSize: (j['pageSize'] as num?)?.toInt() ?? 24,
      totalCount: (j['totalCount'] as num?)?.toInt() ?? 0,
      facets: facets is List
          ? facets
              .whereType<Map>()
              .map((m) => SearchFacet.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      sortOptions: sortOpts is List
          ? sortOpts
              .whereType<Map>()
              .map((m) =>
                  SearchSortOption.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      suggestions: sugg is List
          ? sugg.whereType<String>().toList(growable: false)
          : const [],
    );
  }
}

/// `POST /v1/customer/search/lookup` request payload. Exactly one of
/// [sku] / [barcode] should be populated.
@immutable
class LookupRequest {
  const LookupRequest({
    this.sku,
    this.barcode,
    required this.marketCode,
  }) : assert(sku != null || barcode != null,
            'lookup requires either sku or barcode');

  final String? sku;
  final String? barcode;
  final String marketCode;

  Map<String, Object?> toJson() => {
        if (sku != null) 'sku': sku,
        if (barcode != null) 'barcode': barcode,
        'marketCode': marketCode,
      };
}

@immutable
class LookupMatch {
  const LookupMatch({
    this.productId,
    this.slug,
    this.name,
    required this.kind,
  });

  final String? productId;
  final String? slug;
  final String? name;
  final String kind; // sku | barcode

  factory LookupMatch.fromJson(Map<String, Object?> j) => LookupMatch(
        productId: j['productId'] as String?,
        slug: j['slug'] as String?,
        name: j['name'] as String?,
        kind: j['kind'] as String? ?? 'sku',
      );
}

@immutable
class LookupResult {
  const LookupResult({required this.matched, this.match});
  final bool matched;
  final LookupMatch? match;

  factory LookupResult.fromJson(Map<String, Object?> j) {
    final m = j['match'];
    return LookupResult(
      matched: j['matched'] as bool? ?? false,
      match:
          m is Map ? LookupMatch.fromJson(Map<String, Object?>.from(m)) : null,
    );
  }
}
