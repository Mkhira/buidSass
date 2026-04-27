import 'package:customer_flutter/features/cart/data/cart_view_models.dart';
import 'package:customer_flutter/features/cart/services/cart_merge_service.dart';
import 'package:flutter_test/flutter_test.dart';

CartLineViewModel _line(String id, int qty, {bool restricted = false}) {
  return CartLineViewModel(
    productId: id,
    sku: id,
    name: id,
    quantity: qty,
    unitPriceMinor: 100,
    lineSubtotalMinor: 100 * qty,
    restrictedAndUnverified: restricted,
  );
}

void main() {
  const service = CartMergeService(maxQtyPerOrder: 5);

  test('overlapping lines sum quantities', () {
    final out = service.merge(
      guest: [_line('a', 2)],
      authenticated: [_line('a', 1)],
      restrictedProductIds: const {},
      outOfStockProductIds: const {},
      isCustomerVerified: false,
    );
    expect(out.lines.single.productId, 'a');
    expect(out.lines.single.quantity, 3);
    expect(out.conflicts, isEmpty);
  });

  test('quantity over cap is capped + flagged', () {
    final out = service.merge(
      guest: [_line('a', 4)],
      authenticated: [_line('a', 4)],
      restrictedProductIds: const {},
      outOfStockProductIds: const {},
      isCustomerVerified: false,
    );
    expect(out.lines.single.quantity, 5);
    expect(out.conflicts.single.reason, 'quantity_capped');
  });

  test('out-of-stock products are dropped + flagged', () {
    final out = service.merge(
      guest: [_line('a', 1)],
      authenticated: [_line('b', 1)],
      restrictedProductIds: const {},
      outOfStockProductIds: const {'a'},
      isCustomerVerified: false,
    );
    expect(out.lines.map((l) => l.productId), ['b']);
    expect(out.conflicts.single.reason, 'out_of_stock');
  });

  test('restricted unverified lines are flagged', () {
    final out = service.merge(
      guest: [_line('a', 1)],
      authenticated: [],
      restrictedProductIds: const {'a'},
      outOfStockProductIds: const {},
      isCustomerVerified: false,
    );
    expect(out.lines.single.restrictedAndUnverified, isTrue);
    expect(out.conflicts.single.reason, 'now_restricted_and_unverified');
  });

  test('verified customer keeps restricted lines clean', () {
    final out = service.merge(
      guest: [_line('a', 1)],
      authenticated: [],
      restrictedProductIds: const {'a'},
      outOfStockProductIds: const {},
      isCustomerVerified: true,
    );
    expect(out.lines.single.restrictedAndUnverified, isFalse);
    expect(out.conflicts, isEmpty);
  });
}
