import 'package:flutter/foundation.dart';

/// Single client-side cart line. Persisted to `shared_preferences` under
/// `cart.v1` per Phase 4 data-model.md "Local cart schema".
///
/// The `priceHint` is the price snapshot captured at add-to-cart time and
/// is used **only** for offline display — actual totals come from the
/// server's `POST /customer/pricing/price-cart` preview (BR-2). A drift
/// between [unitPriceMinor] and the latest preview is normal and surfaces
/// through the totals row, not by mutating this snapshot.
@immutable
class CartStoreLine {
  const CartStoreLine({
    required this.productId,
    required this.slug,
    required this.name,
    required this.imageUrl,
    required this.qty,
    required this.unitPriceMinor,
    required this.currency,
    this.isRestricted = false,
  });

  final String productId;
  final String slug;
  final String name;
  final String imageUrl;
  final int qty;
  final int unitPriceMinor;
  final String currency;
  final bool isRestricted;

  CartStoreLine copyWith({int? qty}) => CartStoreLine(
        productId: productId,
        slug: slug,
        name: name,
        imageUrl: imageUrl,
        qty: qty ?? this.qty,
        unitPriceMinor: unitPriceMinor,
        currency: currency,
        isRestricted: isRestricted,
      );

  Map<String, Object?> toJson() => {
        'productId': productId,
        'slug': slug,
        'name': name,
        'imageUrl': imageUrl,
        'qty': qty,
        'unitPriceMinor': unitPriceMinor,
        'currency': currency,
        'isRestricted': isRestricted,
      };

  factory CartStoreLine.fromJson(Map<String, Object?> j) => CartStoreLine(
        productId: j['productId'] as String? ?? '',
        slug: j['slug'] as String? ?? '',
        name: j['name'] as String? ?? '',
        imageUrl: j['imageUrl'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPriceMinor: (j['unitPriceMinor'] as num?)?.toInt() ?? 0,
        currency: j['currency'] as String? ?? '',
        isRestricted: j['isRestricted'] as bool? ?? false,
      );
}

/// Full client-side cart snapshot — what `CartStore.read()` returns.
@immutable
class CartSnapshot {
  const CartSnapshot({
    this.lines = const [],
    this.couponCode,
    this.updatedAt,
  });

  final List<CartStoreLine> lines;
  final String? couponCode;
  final DateTime? updatedAt;

  bool get isEmpty => lines.isEmpty;
  int get totalQty => lines.fold(0, (sum, l) => sum + l.qty);

  CartSnapshot copyWith({
    List<CartStoreLine>? lines,
    String? couponCode,
    bool clearCoupon = false,
    DateTime? updatedAt,
  }) {
    return CartSnapshot(
      lines: lines ?? this.lines,
      couponCode: clearCoupon ? null : (couponCode ?? this.couponCode),
      updatedAt: updatedAt ?? this.updatedAt,
    );
  }

  Map<String, Object?> toJson() => {
        'lines': lines.map((l) => l.toJson()).toList(growable: false),
        if (couponCode != null) 'couponCode': couponCode,
        if (updatedAt != null) 'updatedAt': updatedAt!.toIso8601String(),
      };

  factory CartSnapshot.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    return CartSnapshot(
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) => CartStoreLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      couponCode: j['couponCode'] as String?,
      updatedAt: j['updatedAt'] is String
          ? DateTime.tryParse(j['updatedAt']! as String)
          : null,
    );
  }
}
