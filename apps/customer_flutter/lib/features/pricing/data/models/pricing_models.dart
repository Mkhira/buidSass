import 'package:flutter/foundation.dart';

import '../../../catalog/data/models/catalog_models.dart' show CatalogMoney;

/// Buyer kind sent to the pricing engine. Drives tier selection per
/// Principle 10 (centralized pricing logic).
enum PricingBuyerKind {
  consumer('consumer'),
  business('business'),
  guest('guest');

  const PricingBuyerKind(this.wire);
  final String wire;
}

@immutable
class PricingLineRequest {
  const PricingLineRequest({required this.productId, required this.qty});

  final String productId;
  final int qty;

  Map<String, Object?> toJson() => {'productId': productId, 'qty': qty};
}

@immutable
class PricingRequest {
  const PricingRequest({
    required this.lines,
    required this.marketCode,
    required this.buyerKind,
    this.couponCode,
  });

  final List<PricingLineRequest> lines;
  final String marketCode;
  final PricingBuyerKind buyerKind;
  final String? couponCode;

  Map<String, Object?> toJson() => {
        'lines': lines.map((l) => l.toJson()).toList(growable: false),
        'marketCode': marketCode,
        'buyerKind': buyerKind.wire,
        if (couponCode != null && couponCode!.isNotEmpty)
          'couponCode': couponCode,
      };
}

@immutable
class PricedLine {
  const PricedLine({
    required this.productId,
    required this.qty,
    required this.unitPrice,
    required this.discount,
    required this.lineTotal,
    required this.tierLabel,
  });

  factory PricedLine.fromJson(Map<String, Object?> json) {
    return PricedLine(
      productId: json['productId']?.toString() ?? '',
      qty: (json['qty'] as num?)?.toInt() ?? 0,
      unitPrice: _money(json, 'unitPrice'),
      discount: _money(json, 'discount'),
      lineTotal: _money(json, 'lineTotal'),
      tierLabel: json['tierLabel']?.toString() ?? '',
    );
  }

  final String productId;
  final int qty;
  final CatalogMoney unitPrice;
  final CatalogMoney discount;
  final CatalogMoney lineTotal;

  /// `consumer | business` — surfaces tier on the PDP "B2B price" badge.
  final String tierLabel;
}

@immutable
class AppliedPromotion {
  const AppliedPromotion({
    required this.code,
    required this.amount,
    required this.kind,
  });

  factory AppliedPromotion.fromJson(Map<String, Object?> json) {
    return AppliedPromotion(
      code: json['code']?.toString() ?? '',
      amount: _money(json, 'amount'),
      kind: json['kind']?.toString() ?? '',
    );
  }

  final String code;
  final CatalogMoney amount;

  /// `coupon | promotion | bundle`.
  final String kind;
}

@immutable
class PriceQuote {
  const PriceQuote({
    required this.total,
    required this.lines,
    required this.appliedPromotions,
    required this.explanationToken,
  });

  factory PriceQuote.fromJson(Map<String, Object?> json) {
    final rawLines = json['lines'];
    final lines = rawLines is List
        ? rawLines
            .whereType<Map>()
            .map((m) => PricedLine.fromJson(Map<String, Object?>.from(m)))
            .toList(growable: false)
        : const <PricedLine>[];
    final rawPromos = json['appliedPromotions'];
    final promos = rawPromos is List
        ? rawPromos
            .whereType<Map>()
            .map((m) => AppliedPromotion.fromJson(Map<String, Object?>.from(m)))
            .toList(growable: false)
        : const <AppliedPromotion>[];
    return PriceQuote(
      total: _money(json, 'total'),
      lines: lines,
      appliedPromotions: promos,
      explanationToken: json['explanationToken']?.toString() ?? '',
    );
  }

  final CatalogMoney total;
  final List<PricedLine> lines;
  final List<AppliedPromotion> appliedPromotions;

  /// Audit token referenced by Principle 10 "totals MUST be explainable".
  final String explanationToken;
}

CatalogMoney _money(Map<String, Object?> json, String key) =>
    CatalogMoney.fromJson(json[key]);
