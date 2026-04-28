import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/catalog/bloc/product_detail_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_repository.dart';
import 'package:customer_flutter/features/catalog/data/catalog_view_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements CatalogRepository {}

ProductDetailViewModel _vm({
  bool restricted = false,
  String stock = 'inStock',
}) {
  return ProductDetailViewModel(
    id: 'p1',
    sku: 's1',
    name: 'Test',
    description: 'd',
    mediaUrls: const [],
    attributes: const {},
    priceBreakdown: const PriceBreakdown(
      unitPriceMinor: 1000,
      discountMinor: 0,
      taxMinor: 150,
      totalMinor: 1150,
      currency: 'SAR',
    ),
    stockSignal: StockSignal(value: stock),
    isRestricted: restricted,
    restrictedRationale: restricted ? 'rx' : null,
  );
}

void main() {
  late _MockRepo repo;
  setUp(() => repo = _MockRepo());

  blocTest<ProductDetailBloc, ProductDetailState>(
    'normal product loads -> ProductDetailLoaded',
    build: () {
      when(() => repo.fetchDetail('p1')).thenAnswer((_) async => _vm());
      return ProductDetailBloc(repository: repo, isCustomerVerified: false);
    },
    act: (b) => b.add(const ProductRequested('p1')),
    expect: () => [isA<ProductDetailLoading>(), isA<ProductDetailLoaded>()],
  );

  blocTest<ProductDetailBloc, ProductDetailState>(
    'restricted + unverified -> Restricted',
    build: () {
      when(() => repo.fetchDetail('p1'))
          .thenAnswer((_) async => _vm(restricted: true));
      return ProductDetailBloc(repository: repo, isCustomerVerified: false);
    },
    act: (b) => b.add(const ProductRequested('p1')),
    expect: () => [isA<ProductDetailLoading>(), isA<ProductDetailRestricted>()],
  );

  blocTest<ProductDetailBloc, ProductDetailState>(
    'restricted + verified -> Loaded',
    build: () {
      when(() => repo.fetchDetail('p1'))
          .thenAnswer((_) async => _vm(restricted: true));
      return ProductDetailBloc(repository: repo, isCustomerVerified: true);
    },
    act: (b) => b.add(const ProductRequested('p1')),
    expect: () => [isA<ProductDetailLoading>(), isA<ProductDetailLoaded>()],
  );

  blocTest<ProductDetailBloc, ProductDetailState>(
    'out of stock -> OutOfStock',
    build: () {
      when(() => repo.fetchDetail('p1'))
          .thenAnswer((_) async => _vm(stock: 'outOfStock'));
      return ProductDetailBloc(repository: repo, isCustomerVerified: true);
    },
    act: (b) => b.add(const ProductRequested('p1')),
    expect: () => [isA<ProductDetailLoading>(), isA<ProductDetailOutOfStock>()],
  );

  blocTest<ProductDetailBloc, ProductDetailState>(
    'fetch error -> Error',
    build: () {
      when(() => repo.fetchDetail('p1')).thenThrow(Exception('boom'));
      return ProductDetailBloc(repository: repo, isCustomerVerified: true);
    },
    act: (b) => b.add(const ProductRequested('p1')),
    expect: () => [isA<ProductDetailLoading>(), isA<ProductDetailError>()],
  );
}
