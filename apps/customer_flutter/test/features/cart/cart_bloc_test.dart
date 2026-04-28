import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/auth/auth_session_bloc.dart';
import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:customer_flutter/core/cart/anonymous_cart_token_store.dart';
import 'package:customer_flutter/features/cart/bloc/cart_bloc.dart';
import 'package:customer_flutter/features/cart/data/cart_repository.dart';
import 'package:customer_flutter/features/cart/data/cart_view_models.dart';
import 'package:customer_flutter/features/catalog/data/catalog_view_models.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements CartRepository {}

class _MockTokenStore extends Mock implements AnonymousCartTokenStore {}

class _MockStorage extends Mock implements FlutterSecureStorage {}

CartViewModel _cart({List<CartLineViewModel> lines = const []}) {
  return CartViewModel(
    revision: 1,
    lines: lines,
    totals: const PriceBreakdown(
      unitPriceMinor: 0,
      discountMinor: 0,
      taxMinor: 0,
      totalMinor: 0,
      currency: 'SAR',
    ),
    tokenKind: CartTokenKind.anonymous,
  );
}

void main() {
  late _MockRepo repo;
  late _MockTokenStore tokens;
  late AuthSessionBloc auth;

  setUp(() {
    repo = _MockRepo();
    tokens = _MockTokenStore();
    final storage = _MockStorage();
    when(() => storage.read(key: any(named: 'key')))
        .thenAnswer((_) async => null);
    when(() =>
            storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key'))).thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
    auth = AuthSessionBloc(tokenStore: SecureTokenStore(storage: storage));
    when(tokens.readToken).thenAnswer((_) async => 'tok');
  });

  blocTest<CartBloc, CartState>(
    'CartRefreshed -> Loaded for non-empty cart',
    build: () {
      when(repo.fetchCart).thenAnswer((_) async => _cart(lines: [
            const CartLineViewModel(
              productId: 'a',
              sku: 'a',
              name: 'A',
              quantity: 1,
              unitPriceMinor: 100,
              lineSubtotalMinor: 100,
            ),
          ]));
      return CartBloc(
        repository: repo,
        tokenStore: tokens,
        authSessionBloc: auth,
      );
    },
    act: (b) => b.add(const CartRefreshed()),
    expect: () => [isA<CartLoading>(), isA<CartLoaded>()],
  );

  blocTest<CartBloc, CartState>(
    'CartRefreshed -> Empty for empty cart',
    build: () {
      when(repo.fetchCart).thenAnswer((_) async => _cart());
      return CartBloc(
        repository: repo,
        tokenStore: tokens,
        authSessionBloc: auth,
      );
    },
    act: (b) => b.add(const CartRefreshed()),
    expect: () => [isA<CartLoading>(), isA<CartEmpty>()],
  );

  blocTest<CartBloc, CartState>(
    'LineQuantityChanged -> Mutating -> Loaded',
    build: () {
      when(() => repo.updateLineQuantity(
            productId: any(named: 'productId'),
            quantity: any(named: 'quantity'),
          )).thenAnswer((_) async => _cart(lines: [
            const CartLineViewModel(
              productId: 'a',
              sku: 'a',
              name: 'A',
              quantity: 2,
              unitPriceMinor: 100,
              lineSubtotalMinor: 200,
            ),
          ]));
      return CartBloc(
        repository: repo,
        tokenStore: tokens,
        authSessionBloc: auth,
      );
    },
    seed: () => CartLoaded(_cart(lines: [
      const CartLineViewModel(
        productId: 'a',
        sku: 'a',
        name: 'A',
        quantity: 1,
        unitPriceMinor: 100,
        lineSubtotalMinor: 100,
      ),
    ])),
    act: (b) => b.add(const LineQuantityChanged(productId: 'a', quantity: 2)),
    expect: () => [isA<CartMutating>(), isA<CartLoaded>()],
  );

  blocTest<CartBloc, CartState>(
    'LineQuantityChanged -> OutOfSync on revision mismatch',
    build: () {
      when(() => repo.updateLineQuantity(
            productId: any(named: 'productId'),
            quantity: any(named: 'quantity'),
          )).thenThrow(const CartRevisionMismatchException());
      return CartBloc(
        repository: repo,
        tokenStore: tokens,
        authSessionBloc: auth,
      );
    },
    seed: () => CartLoaded(_cart()),
    act: (b) => b.add(const LineQuantityChanged(productId: 'a', quantity: 2)),
    expect: () => [isA<CartMutating>(), isA<CartOutOfSync>()],
  );
}
