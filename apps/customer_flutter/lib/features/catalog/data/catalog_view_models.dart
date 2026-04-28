import 'package:flutter/foundation.dart';

@immutable
class FacetOption {
  const FacetOption({
    required this.value,
    required this.labelKey,
    required this.count,
  });
  final String value;
  final String labelKey;
  final int count;
}

@immutable
class Facet {
  const Facet({required this.kind, required this.options});
  final String kind;
  final List<FacetOption> options;
}

@immutable
class ProductListingItem {
  const ProductListingItem({
    required this.id,
    required this.name,
    required this.thumbnailUrl,
    required this.priceMinor,
    required this.currency,
    required this.isRestricted,
    required this.inStock,
  });
  final String id;
  final String name;
  final String thumbnailUrl;
  final int priceMinor;
  final String currency;
  final bool isRestricted;
  final bool inStock;
}

@immutable
class ListingPage {
  const ListingPage({
    required this.items,
    required this.facets,
    required this.nextCursor,
  });
  final List<ProductListingItem> items;
  final List<Facet> facets;
  final String? nextCursor;

  bool get hasMore => nextCursor != null;
}

@immutable
class StockSignal {
  const StockSignal({required this.value});
  final String value; // inStock | low | outOfStock

  bool get isOutOfStock => value == 'outOfStock';
}

@immutable
class PriceBreakdown {
  const PriceBreakdown({
    required this.unitPriceMinor,
    required this.discountMinor,
    required this.taxMinor,
    required this.totalMinor,
    required this.currency,
  });
  final int unitPriceMinor;
  final int discountMinor;
  final int taxMinor;
  final int totalMinor;
  final String currency;
}

@immutable
class ProductDetailViewModel {
  const ProductDetailViewModel({
    required this.id,
    required this.sku,
    required this.name,
    required this.description,
    required this.mediaUrls,
    required this.attributes,
    required this.priceBreakdown,
    required this.stockSignal,
    required this.isRestricted,
    required this.restrictedRationale,
  });
  final String id;
  final String sku;
  final String name;
  final String description;
  final List<String> mediaUrls;
  final Map<String, String> attributes;
  final PriceBreakdown priceBreakdown;
  final StockSignal stockSignal;
  final bool isRestricted;
  final String? restrictedRationale;
}
