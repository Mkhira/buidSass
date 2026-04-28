import 'package:customer_flutter/features/catalog/data/catalog_view_models.dart';
import 'package:customer_flutter/features/orders/data/order_view_models.dart';
import 'package:customer_flutter/features/orders/services/reorder_service.dart';
import 'package:flutter_test/flutter_test.dart';

OrderLineViewModel _line(String id, {required bool inStock}) {
  return OrderLineViewModel(
    productId: id,
    sku: id,
    name: id,
    quantity: 1,
    unitPriceMinor: 100,
    lineSubtotalMinor: 100,
    inStock: inStock,
  );
}

OrderDetailViewModel _order(List<OrderLineViewModel> lines) {
  return OrderDetailViewModel(
    id: 'o',
    orderNumber: 'O',
    placedAt: DateTime(2026, 4, 1),
    orderState: 'placed',
    paymentState: 'authorized',
    fulfillmentState: 'pending',
    refundState: 'none',
    lines: lines,
    totals: const PriceBreakdown(
      unitPriceMinor: 0,
      discountMinor: 0,
      taxMinor: 0,
      totalMinor: 0,
      currency: 'SAR',
    ),
    timeline: const [],
    refundEligibility: const RefundEligibility(canRequest: false),
  );
}

void main() {
  const service = ReorderService();

  test('all lines in-stock -> all eligible, no oos', () {
    final r = service.plan(_order([
      _line('a', inStock: true),
      _line('b', inStock: true),
    ]));
    expect(r.eligibleProductIds, ['a', 'b']);
    expect(r.outOfStockProductIds, isEmpty);
    expect(r.hasOutOfStock, isFalse);
  });

  test('mixed lines -> partition + hasOutOfStock', () {
    final r = service.plan(_order([
      _line('a', inStock: true),
      _line('b', inStock: false),
    ]));
    expect(r.eligibleProductIds, ['a']);
    expect(r.outOfStockProductIds, ['b']);
    expect(r.hasOutOfStock, isTrue);
  });

  test('all out-of-stock -> empty eligible', () {
    final r = service.plan(_order([_line('a', inStock: false)]));
    expect(r.eligibleProductIds, isEmpty);
    expect(r.outOfStockProductIds, ['a']);
  });
}
