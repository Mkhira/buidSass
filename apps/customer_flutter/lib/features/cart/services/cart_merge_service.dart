import '../data/cart_view_models.dart';

class MergedCart {
  const MergedCart({required this.lines, required this.conflicts});
  final List<CartLineViewModel> lines;
  final List<CartConflictReport> conflicts;
}

/// FR-013b — client-side fallback merge invoked when spec 009's claim
/// endpoint isn't shipped or returns a documented gap. The service is
/// dormant by default; CartBloc only invokes it on a gap response.
class CartMergeService {
  const CartMergeService({this.maxQtyPerOrder = 99});

  /// Per-product cap. The real limit comes from spec 005's `max_qty_per_order`
  /// per product; a flat default keeps the merge deterministic until 005's
  /// detail VM lands.
  final int maxQtyPerOrder;

  MergedCart merge({
    required List<CartLineViewModel> guest,
    required List<CartLineViewModel> authenticated,
    required Set<String> restrictedProductIds,
    required Set<String> outOfStockProductIds,
    required bool isCustomerVerified,
  }) {
    final byId = <String, CartLineViewModel>{};
    for (final line in [...authenticated, ...guest]) {
      final existing = byId[line.productId];
      if (existing == null) {
        byId[line.productId] = line;
      } else {
        // Sum quantities; guest metadata wins on overlap (per FR-013b).
        final summed = existing.copyWith(
          quantity: existing.quantity + line.quantity,
        );
        byId[line.productId] = summed;
      }
    }

    final conflicts = <CartConflictReport>[];
    final lines = <CartLineViewModel>[];
    for (final line in byId.values) {
      var current = line;
      if (current.quantity > maxQtyPerOrder) {
        current = current.copyWith(quantity: maxQtyPerOrder);
        conflicts.add(CartConflictReport(
          productId: current.productId,
          reason: 'quantity_capped',
        ));
      }
      if (outOfStockProductIds.contains(current.productId)) {
        conflicts.add(CartConflictReport(
          productId: current.productId,
          reason: 'out_of_stock',
        ));
        continue;
      }
      if (restrictedProductIds.contains(current.productId) &&
          !isCustomerVerified) {
        current = current.copyWith(restrictedAndUnverified: true);
        conflicts.add(CartConflictReport(
          productId: current.productId,
          reason: 'now_restricted_and_unverified',
        ));
      }
      lines.add(current);
    }
    return MergedCart(lines: lines, conflicts: conflicts);
  }
}
