import 'package:flutter/foundation.dart';

import '../data/order_view_models.dart';

@immutable
class ReorderResult {
  const ReorderResult({
    required this.eligibleProductIds,
    required this.outOfStockProductIds,
  });
  final List<String> eligibleProductIds;
  final List<String> outOfStockProductIds;

  bool get hasOutOfStock => outOfStockProductIds.isNotEmpty;
}

/// FR-027 — given an `OrderDetailViewModel`, partitions its lines into
/// in-stock (eligible to add to a fresh cart) and out-of-stock (surfaced as
/// a notice). The actual cart-add operation is owned by `CartBloc`; this
/// service is the deterministic, testable selection step.
class ReorderService {
  const ReorderService();

  ReorderResult plan(OrderDetailViewModel order) {
    final eligible = <String>[];
    final oos = <String>[];
    for (final line in order.lines) {
      if (line.inStock) {
        eligible.add(line.productId);
      } else {
        oos.add(line.productId);
      }
    }
    return ReorderResult(
      eligibleProductIds: eligible,
      outOfStockProductIds: oos,
    );
  }
}
