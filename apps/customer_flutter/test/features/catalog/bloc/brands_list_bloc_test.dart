import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/bloc/brands_list_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_gateway.dart';
import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockCatalog extends Mock implements CatalogGateway {}

void main() {
  late _MockCatalog catalog;
  setUp(() => catalog = _MockCatalog());

  blocTest<BrandsListBloc, BrandsListState>(
    'Loading -> Loaded on non-empty',
    build: () {
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const [
                CatalogBrand(
                  id: 'b-1',
                  slug: 'brand-x',
                  name: LocalizedText(en: 'Brand X'),
                ),
              ]);
      return BrandsListBloc(gateway: catalog);
    },
    act: (b) => b.add(const BrandsListRequested()),
    expect: () => [
      isA<BrandsListLoading>(),
      isA<BrandsListLoaded>(),
    ],
  );

  blocTest<BrandsListBloc, BrandsListState>(
    'Loading -> Empty on empty',
    build: () {
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const []);
      return BrandsListBloc(gateway: catalog);
    },
    act: (b) => b.add(const BrandsListRequested()),
    expect: () => [isA<BrandsListLoading>(), isA<BrandsListEmpty>()],
  );

  blocTest<BrandsListBloc, BrandsListState>(
    'Loading -> Error on Failure',
    build: () {
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenThrow(const ServerFailure(
              code: 'server.boom',
              message: 'x',
              correlationId: 'c-1'));
      return BrandsListBloc(gateway: catalog);
    },
    act: (b) => b.add(const BrandsListRequested()),
    expect: () => [isA<BrandsListLoading>(), isA<BrandsListError>()],
  );
}
