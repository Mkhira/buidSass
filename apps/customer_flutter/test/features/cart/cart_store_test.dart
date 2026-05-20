import 'package:customer_flutter/features/cart/data/cart_store.dart';
import 'package:customer_flutter/features/cart/data/models/cart_store_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

CartStoreLine _line(String id, {int qty = 1, int priceMinor = 12000}) =>
    CartStoreLine(
      productId: id,
      slug: id,
      name: 'P-$id',
      imageUrl: '',
      qty: qty,
      unitPriceMinor: priceMinor,
      currency: 'SAR',
    );

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('addLine appends a new product, increments existing', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = CartStore(prefs: prefs);
    await store.load();
    await store.addLine(_line('a'));
    expect(store.snapshot.lines.single.productId, 'a');
    await store.addLine(_line('a'));
    expect(store.snapshot.lines.single.qty, 2);
    await store.addLine(_line('b'));
    expect(store.snapshot.lines, hasLength(2));
  });

  test('setQty(0) removes the line', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = CartStore(prefs: prefs);
    await store.load();
    await store.addLine(_line('a'));
    await store.setQty(productId: 'a', qty: 0);
    expect(store.snapshot.lines, isEmpty);
  });

  test('cart survives store recreate via shared_preferences', () async {
    var prefs = await SharedPreferences.getInstance();
    final first = CartStore(prefs: prefs);
    await first.load();
    await first.addLine(_line('a', qty: 3));
    await first.applyCoupon('SAVE10');
    await first.dispose();

    // Simulate a "process restart" by re-instantiating the store against
    // the same in-memory prefs (the same atomic write the OS sees in
    // production).
    prefs = await SharedPreferences.getInstance();
    final second = CartStore(prefs: prefs);
    await second.load();
    expect(second.snapshot.lines.single.qty, 3);
    expect(second.snapshot.couponCode, 'SAVE10');
  });

  test('clear empties cart and persists', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = CartStore(prefs: prefs);
    await store.load();
    await store.addLine(_line('a'));
    await store.clear();
    expect(store.snapshot.lines, isEmpty);
    expect(prefs.getString('cart.v1'), isNotNull);
  });

  test('coupon clear / apply round-trip', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = CartStore(prefs: prefs);
    await store.load();
    await store.applyCoupon('X');
    expect(store.snapshot.couponCode, 'X');
    await store.clearCoupon();
    expect(store.snapshot.couponCode, isNull);
  });

  test('stream emits on every mutation', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = CartStore(prefs: prefs);
    await store.load();
    final events = <CartSnapshot>[];
    final sub = store.stream.listen(events.add);
    await store.addLine(_line('a'));
    await store.addLine(_line('b'));
    await store.setQty(productId: 'a', qty: 5);
    await Future<void>.delayed(Duration.zero);
    expect(events, hasLength(3));
    await sub.cancel();
  });
}
