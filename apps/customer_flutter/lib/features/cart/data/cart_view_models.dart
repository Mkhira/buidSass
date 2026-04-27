import 'package:flutter/foundation.dart';

import '../../catalog/data/catalog_view_models.dart';

@immutable
class CartLineViewModel {
  const CartLineViewModel({
    required this.productId,
    required this.sku,
    required this.name,
    required this.quantity,
    required this.unitPriceMinor,
    required this.lineSubtotalMinor,
    this.mediaThumbUrl,
    this.restrictedAndUnverified = false,
  });
  final String productId;
  final String sku;
  final String name;
  final int quantity;
  final int unitPriceMinor;
  final int lineSubtotalMinor;
  final String? mediaThumbUrl;
  final bool restrictedAndUnverified;

  CartLineViewModel copyWith({
    int? quantity,
    bool? restrictedAndUnverified,
  }) {
    return CartLineViewModel(
      productId: productId,
      sku: sku,
      name: name,
      quantity: quantity ?? this.quantity,
      unitPriceMinor: unitPriceMinor,
      lineSubtotalMinor: lineSubtotalMinor,
      mediaThumbUrl: mediaThumbUrl,
      restrictedAndUnverified:
          restrictedAndUnverified ?? this.restrictedAndUnverified,
    );
  }
}

@immutable
class CartViewModel {
  const CartViewModel({
    required this.revision,
    required this.lines,
    required this.totals,
    required this.tokenKind,
  });
  final int revision;
  final List<CartLineViewModel> lines;
  final PriceBreakdown totals;
  final CartTokenKind tokenKind;

  bool get isEmpty => lines.isEmpty;

  List<CartLineViewModel> get verificationRevokedLines =>
      lines.where((l) => l.restrictedAndUnverified).toList(growable: false);
}

enum CartTokenKind { anonymous, authenticated }

@immutable
class CartConflictReport {
  const CartConflictReport({
    required this.productId,
    required this.reason,
  });
  final String productId;
  final String reason; // quantity_capped | now_restricted_and_unverified | out_of_stock
}

@immutable
class CartClaimOutcome {
  const CartClaimOutcome({
    required this.cart,
    required this.conflicts,
    this.gap = false,
  });

  /// Construct a "gap" outcome — spec 009's claim endpoint isn't shipped or
  /// returned a documented gap. The CartBloc falls back to client-side merge
  /// per FR-013b.
  const CartClaimOutcome.gap()
      : cart = const CartViewModel(
          revision: 0,
          lines: <CartLineViewModel>[],
          totals: PriceBreakdown(
            unitPriceMinor: 0,
            discountMinor: 0,
            taxMinor: 0,
            totalMinor: 0,
            currency: 'SAR',
          ),
          tokenKind: CartTokenKind.authenticated,
        ),
        conflicts = const <CartConflictReport>[],
        gap = true;

  final CartViewModel cart;
  final List<CartConflictReport> conflicts;
  final bool gap;
}
