import 'package:flutter/foundation.dart';

/// Localized string. Categories and product names come back as
/// `{ "ar": "...", "en": "..." }` from `/v1/customer/catalog/categories`
/// and similar surfaces (see Phase 2 spec §S-2.1 response shape).
@immutable
class LocalizedText {
  const LocalizedText({this.ar, this.en});

  factory LocalizedText.fromJson(Object? raw) {
    if (raw is String) return LocalizedText(ar: raw, en: raw);
    if (raw is Map) {
      return LocalizedText(
        ar: raw['ar']?.toString(),
        en: raw['en']?.toString(),
      );
    }
    return const LocalizedText();
  }

  final String? ar;
  final String? en;

  /// Pick the value for [locale] code (`'ar'` / `'en'`), falling back to the
  /// other locale, then to empty string. Mirrors the server's resolution
  /// order so the mobile layer never displays raw locale-keyed JSON.
  String resolve(String locale) {
    final lower = locale.toLowerCase();
    if (lower.startsWith('ar')) return ar ?? en ?? '';
    return en ?? ar ?? '';
  }
}

@immutable
class CatalogCategory {
  const CatalogCategory({
    required this.id,
    required this.slug,
    required this.name,
    this.iconUrl,
    this.parentId,
  });

  factory CatalogCategory.fromJson(Map<String, Object?> json) {
    return CatalogCategory(
      id: json['id']?.toString() ?? '',
      slug: json['slug']?.toString() ?? '',
      name: LocalizedText.fromJson(json['name']),
      iconUrl: json['iconUrl']?.toString(),
      parentId: json['parentId']?.toString(),
    );
  }

  final String id;
  final String slug;
  final LocalizedText name;
  final String? iconUrl;
  final String? parentId;
}

@immutable
class CatalogBrand {
  const CatalogBrand({
    required this.id,
    required this.slug,
    required this.name,
    this.logoUrl,
  });

  factory CatalogBrand.fromJson(Map<String, Object?> json) {
    return CatalogBrand(
      id: json['id']?.toString() ?? '',
      slug: json['slug']?.toString() ?? '',
      name: LocalizedText.fromJson(json['name']),
      logoUrl: json['logoUrl']?.toString(),
    );
  }

  final String id;
  final String slug;
  final LocalizedText name;
  final String? logoUrl;
}

/// Server-supplied sort enum. Matches the OpenAPI contract; the mobile
/// layer never invents sort keys (BR-9).
enum CatalogSort {
  relevance('relevance'),
  priceAsc('price-asc'),
  priceDesc('price-desc'),
  newest('newest');

  const CatalogSort(this.wire);
  final String wire;
}

enum CatalogRestrictedFilter {
  any('any'),
  onlyUnrestricted('only-unrestricted');

  const CatalogRestrictedFilter(this.wire);
  final String wire;
}

@immutable
class CatalogMoney {
  const CatalogMoney({required this.amountMinor, required this.currency});

  /// Reads either `{amountMinor, currency}` or the more common
  /// `{amount: "120.00", currency: "SAR"}` decimal shape. Decimal strings
  /// are converted to minor units assuming 2 fraction digits.
  factory CatalogMoney.fromJson(Object? raw) {
    if (raw is! Map) return const CatalogMoney(amountMinor: 0, currency: '');
    final currency = raw['currency']?.toString() ?? '';
    final minor = raw['amountMinor'];
    if (minor is num) {
      return CatalogMoney(amountMinor: minor.toInt(), currency: currency);
    }
    final amount = raw['amount'];
    if (amount is num) {
      return CatalogMoney(
        amountMinor: (amount * 100).round(),
        currency: currency,
      );
    }
    if (amount is String) {
      final asDouble = double.tryParse(amount);
      if (asDouble != null) {
        return CatalogMoney(
          amountMinor: (asDouble * 100).round(),
          currency: currency,
        );
      }
    }
    return CatalogMoney(amountMinor: 0, currency: currency);
  }

  final int amountMinor;
  final String currency;
}

@immutable
class CatalogProduct {
  const CatalogProduct({
    required this.id,
    required this.slug,
    required this.name,
    required this.thumbnailUrl,
    required this.priceHint,
    required this.isRestricted,
    this.brandSlug,
    this.brandName,
    this.ratingAverage,
    this.ratingCount,
  });

  factory CatalogProduct.fromJson(Map<String, Object?> json) {
    return CatalogProduct(
      id: json['id']?.toString() ?? '',
      slug: json['slug']?.toString() ?? '',
      name: LocalizedText.fromJson(json['name']),
      thumbnailUrl: json['thumbnailUrl']?.toString() ?? '',
      priceHint: CatalogMoney.fromJson(json['priceHint']),
      isRestricted: json['restricted'] == true || json['isRestricted'] == true,
      brandSlug: json['brandSlug']?.toString(),
      brandName: json['brandName'] == null
          ? null
          : LocalizedText.fromJson(json['brandName']),
      ratingAverage: (json['ratingAverage'] as num?)?.toDouble(),
      ratingCount: (json['ratingCount'] as num?)?.toInt(),
    );
  }

  final String id;
  final String slug;
  final LocalizedText name;
  final String thumbnailUrl;

  /// Catalog-supplied price hint. UI shows this immediately on the PDP and
  /// replaces it with the engine result from PricingGateway when the
  /// preview returns (BR-2 / BR-10).
  final CatalogMoney priceHint;
  final bool isRestricted;
  final String? brandSlug;
  final LocalizedText? brandName;
  final double? ratingAverage;
  final int? ratingCount;
}

@immutable
class CatalogProductPage {
  const CatalogProductPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalItems,
  });

  factory CatalogProductPage.fromJson(Map<String, Object?> json) {
    final rawItems = json['items'];
    final items = rawItems is List
        ? rawItems
            .whereType<Map>()
            .map((m) => CatalogProduct.fromJson(Map<String, Object?>.from(m)))
            .toList(growable: false)
        : const <CatalogProduct>[];
    return CatalogProductPage(
      items: items,
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? items.length,
      totalItems: (json['totalItems'] as num?)?.toInt() ?? items.length,
    );
  }

  final List<CatalogProduct> items;
  final int page;
  final int pageSize;
  final int totalItems;

  bool get hasMore => page * pageSize < totalItems;
}

@immutable
class CatalogProductDetail {
  const CatalogProductDetail({
    required this.id,
    required this.slug,
    required this.sku,
    required this.name,
    required this.description,
    required this.mediaUrls,
    required this.attributes,
    required this.priceHint,
    required this.isRestricted,
    this.brandSlug,
    this.brandName,
    this.restrictedRationale,
  });

  factory CatalogProductDetail.fromJson(Map<String, Object?> json) {
    final rawMedia = json['mediaUrls'];
    final media = rawMedia is List
        ? rawMedia.whereType<String>().toList(growable: false)
        : const <String>[];
    final rawAttrs = json['attributes'];
    final attrs = <String, LocalizedText>{};
    if (rawAttrs is Map) {
      rawAttrs.forEach((k, v) {
        attrs[k.toString()] = LocalizedText.fromJson(v);
      });
    }
    return CatalogProductDetail(
      id: json['id']?.toString() ?? '',
      slug: json['slug']?.toString() ?? '',
      sku: json['sku']?.toString() ?? '',
      name: LocalizedText.fromJson(json['name']),
      description: LocalizedText.fromJson(json['description']),
      mediaUrls: media,
      attributes: attrs,
      priceHint: CatalogMoney.fromJson(json['priceHint']),
      isRestricted: json['restricted'] == true || json['isRestricted'] == true,
      brandSlug: json['brandSlug']?.toString(),
      brandName: json['brandName'] == null
          ? null
          : LocalizedText.fromJson(json['brandName']),
      restrictedRationale: json['restrictedRationale'] == null
          ? null
          : LocalizedText.fromJson(json['restrictedRationale']),
    );
  }

  final String id;
  final String slug;
  final String sku;
  final LocalizedText name;
  final LocalizedText description;
  final List<String> mediaUrls;
  final Map<String, LocalizedText> attributes;
  final CatalogMoney priceHint;
  final bool isRestricted;
  final String? brandSlug;
  final LocalizedText? brandName;
  final LocalizedText? restrictedRationale;
}
