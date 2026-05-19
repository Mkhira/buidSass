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
  /// `{amount: "120.00", currency: "SAR"}` decimal shape. Decimal-string
  /// scaling uses **currency-aware fraction digits**, not a hardcoded
  /// `× 100`, so non-2-decimal currencies (JPY=0, KWD/BHD/OMR=3) decode
  /// correctly across markets. Server payloads MAY override the default
  /// with an explicit `fractionDigits` field on the money object.
  factory CatalogMoney.fromJson(Object? raw) {
    if (raw is! Map) return const CatalogMoney(amountMinor: 0, currency: '');
    final currency = raw['currency']?.toString() ?? '';
    final minor = raw['amountMinor'];
    if (minor is num) {
      return CatalogMoney(amountMinor: minor.toInt(), currency: currency);
    }
    final explicitDigits = raw['fractionDigits'];
    final digits = explicitDigits is num
        ? explicitDigits.toInt()
        : fractionDigitsForCurrency(currency);
    final amount = raw['amount'];
    // String path — parse as decimal lexically (no `double.tryParse`)
    // so we never round-trip through IEEE-754 binary floats. Currency
    // values like 1.005 or KWD's 3-decimal precision must be exact.
    if (amount is String) {
      final parsed = _parseDecimalToMinorUnits(amount, digits);
      if (parsed != null) {
        return CatalogMoney(amountMinor: parsed, currency: currency);
      }
    }
    if (amount is num) {
      // Numeric server payloads are a fallback contract; reduce
      // float exposure by going through the decimal string parser
      // when the value is integral, otherwise accept the IEEE round-
      // trip with a clear warning in the contract docs.
      if (amount is int) {
        return CatalogMoney(
          amountMinor: amount * _pow10(digits),
          currency: currency,
        );
      }
      final parsed = _parseDecimalToMinorUnits(amount.toString(), digits);
      if (parsed != null) {
        return CatalogMoney(amountMinor: parsed, currency: currency);
      }
    }
    return CatalogMoney(amountMinor: 0, currency: currency);
  }

  final int amountMinor;
  final String currency;
}

/// Minor-unit fraction digits per ISO-4217 currency code. Defaults to 2
/// for any currency not listed (the most common case). Exported so UI
/// formatting (e.g. PriceLabel) can render the same way the engine
/// stored the value.
///
/// Source: ISO-4217 published exponents. Zero-decimal currencies span
/// JPY, KRW, VND, etc.; three-decimal cluster is the Gulf dinars
/// (KWD/BHD/OMR/JOD/LYD/TND).
int fractionDigitsForCurrency(String currency) {
  switch (currency.toUpperCase()) {
    case 'JPY':
    case 'KRW':
    case 'VND':
    case 'CLP':
    case 'ISK':
    case 'PYG':
    case 'UGX':
    case 'RWF':
    case 'XAF':
    case 'XOF':
    case 'XPF':
      return 0;
    case 'KWD':
    case 'BHD':
    case 'OMR':
    case 'JOD':
    case 'LYD':
    case 'TND':
      return 3;
    default:
      return 2;
  }
}

int _pow10(int n) {
  var r = 1;
  for (var i = 0; i < n; i++) {
    r *= 10;
  }
  return r;
}

/// Parse a decimal string (e.g. `"120.50"`, `"-1.005"`, `"42"`) directly
/// into integer minor units, **without going through `double`**. Avoids
/// the IEEE-754 rounding artefacts `double.tryParse` introduces on
/// boundary values like `1.005` and on three-decimal currencies.
///
/// Returns null when the input isn't a well-formed decimal.
int? _parseDecimalToMinorUnits(String s, int fractionDigits) {
  if (s.isEmpty) return null;
  var input = s.trim();
  if (input.isEmpty) return null;
  var sign = 1;
  if (input.startsWith('-')) {
    sign = -1;
    input = input.substring(1);
  } else if (input.startsWith('+')) {
    input = input.substring(1);
  }
  if (input.isEmpty) return null;
  final dotIdx = input.indexOf('.');
  String whole;
  String frac;
  if (dotIdx < 0) {
    whole = input;
    frac = '';
  } else {
    whole = input.substring(0, dotIdx);
    frac = input.substring(dotIdx + 1);
  }
  if (whole.isEmpty && frac.isEmpty) return null;
  // Reject non-digit characters early — keeps parsing strict.
  for (final ch in whole.codeUnits) {
    if (ch < 0x30 || ch > 0x39) return null;
  }
  for (final ch in frac.codeUnits) {
    if (ch < 0x30 || ch > 0x39) return null;
  }
  // Right-pad or round the fractional part to `fractionDigits` precision.
  final fracPadded = frac.length >= fractionDigits
      ? frac.substring(0, fractionDigits)
      : frac.padRight(fractionDigits, '0');
  // Banker-style half-up rounding when the input carried more digits
  // than the currency precision (e.g. 1.005 → 1.01 for SAR).
  var rounded = int.tryParse(fracPadded.isEmpty ? '0' : fracPadded);
  if (rounded == null) return null;
  if (frac.length > fractionDigits) {
    final nextDigit = frac.codeUnitAt(fractionDigits) - 0x30;
    if (nextDigit >= 5) rounded += 1;
  }
  final wholeInt = whole.isEmpty ? 0 : int.tryParse(whole);
  if (wholeInt == null) return null;
  final total = wholeInt * _pow10(fractionDigits) + rounded;
  return sign * total;
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
    final attrs = <ProductAttribute>[];
    if (rawAttrs is List) {
      // New shape: `[{ key, label: {ar,en}, value: {ar,en} }, …]`.
      // Preferred because labels themselves are localizable.
      for (final entry in rawAttrs.whereType<Map>()) {
        attrs.add(ProductAttribute.fromJson(
          Map<String, Object?>.from(entry),
        ));
      }
    } else if (rawAttrs is Map) {
      // Back-compat with the older `{ key: value-or-LocalizedText }` shape
      // (used by some seed payloads). Without a server-supplied label we
      // promote the JSON key as the label fallback — the bilingual label
      // arrives once the server upgrades to the new shape.
      rawAttrs.forEach((k, v) {
        attrs.add(ProductAttribute(
          key: k.toString(),
          label: LocalizedText.fromJson(k.toString()),
          value: LocalizedText.fromJson(v),
        ));
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

  /// Spec attributes ordered as the server sent them. Each carries both
  /// a localized label (so AR PDPs render an Arabic attribute name) and
  /// a localized value, addressing Principle 4 (editorial-grade Arabic
  /// everywhere — including the spec table on the PDP).
  final List<ProductAttribute> attributes;
  final CatalogMoney priceHint;
  final bool isRestricted;
  final String? brandSlug;
  final LocalizedText? brandName;
  final LocalizedText? restrictedRationale;
}

@immutable
class ProductAttribute {
  const ProductAttribute({
    required this.key,
    required this.label,
    required this.value,
  });

  factory ProductAttribute.fromJson(Map<String, Object?> json) {
    final key = json['key']?.toString() ?? '';
    return ProductAttribute(
      key: key,
      // Fall back to the stable key when the server omits a label —
      // covers transitional payloads and admin-side keys that aren't
      // editorialized yet.
      label: json['label'] == null
          ? LocalizedText.fromJson(key)
          : LocalizedText.fromJson(json['label']),
      value: LocalizedText.fromJson(json['value']),
    );
  }

  /// Stable identifier (e.g. `weight`, `finish`). Useful for tests and
  /// for analytics; never surfaced to users.
  final String key;

  /// Bilingual user-facing label.
  final LocalizedText label;

  /// Bilingual user-facing value.
  final LocalizedText value;
}
