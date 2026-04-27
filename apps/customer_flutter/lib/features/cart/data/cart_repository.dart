import '../../catalog/data/catalog_view_models.dart';
import 'cart_view_models.dart';

abstract class CartRepository {
  Future<CartViewModel> fetchCart();

  Future<CartViewModel> updateLineQuantity({
    required String productId,
    required int quantity,
  });

  Future<CartViewModel> removeLine(String productId);

  Future<CartClaimOutcome> claimAnonymousCart({required String token});
}

/// Stub adapter — emits an empty cart until spec 009's client is generated.
class StubCartRepository implements CartRepository {
  @override
  Future<CartViewModel> fetchCart() async {
    return _empty;
  }

  @override
  Future<CartViewModel> updateLineQuantity({
    required String productId,
    required int quantity,
  }) async {
    throw const CartGapException();
  }

  @override
  Future<CartViewModel> removeLine(String productId) async {
    throw const CartGapException();
  }

  @override
  Future<CartClaimOutcome> claimAnonymousCart({required String token}) async {
    return const CartClaimOutcome.gap();
  }
}

const CartViewModel _empty = CartViewModel(
  revision: 0,
  lines: <CartLineViewModel>[],
  totals: PriceBreakdown(
    unitPriceMinor: 0,
    discountMinor: 0,
    taxMinor: 0,
    totalMinor: 0,
    currency: 'SAR',
  ),
  tokenKind: CartTokenKind.anonymous,
);

class CartGapException implements Exception {
  const CartGapException();
  @override
  String toString() => 'Cart client gap — escalate to spec 009 (FR-031).';
}

class CartRevisionMismatchException implements Exception {
  const CartRevisionMismatchException();
  @override
  String toString() => 'cart.revision_mismatch';
}
