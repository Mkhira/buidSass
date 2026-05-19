import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/bloc/categories_list_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_gateway.dart';
import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockCatalog extends Mock implements CatalogGateway {}

void main() {
  late _MockCatalog catalog;
  setUp(() => catalog = _MockCatalog());

  blocTest<CategoriesListBloc, CategoriesListState>(
    'Loading -> Loaded on non-empty result',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenAnswer((_) async => const [
                CatalogCategory(
                  id: 'c-1',
                  slug: 'x',
                  name: LocalizedText(en: 'X'),
                ),
              ]);
      return CategoriesListBloc(gateway: catalog);
    },
    act: (b) => b.add(const CategoriesListRequested()),
    expect: () => [
      isA<CategoriesListLoading>(),
      isA<CategoriesListLoaded>()
          .having((s) => s.categories, 'categories', hasLength(1)),
    ],
  );

  blocTest<CategoriesListBloc, CategoriesListState>(
    'Loading -> Empty on empty result',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenAnswer((_) async => const []);
      return CategoriesListBloc(gateway: catalog);
    },
    act: (b) => b.add(const CategoriesListRequested()),
    expect: () => [isA<CategoriesListLoading>(), isA<CategoriesListEmpty>()],
  );

  blocTest<CategoriesListBloc, CategoriesListState>(
    'Loading -> Error on Failure',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenThrow(const OfflineFailure(
              code: 'network.offline', message: 'x', correlationId: 'c-1'));
      return CategoriesListBloc(gateway: catalog);
    },
    act: (b) => b.add(const CategoriesListRequested()),
    expect: () => [isA<CategoriesListLoading>(), isA<CategoriesListError>()],
  );

  blocTest<CategoriesListBloc, CategoriesListState>(
    'market override propagates',
    build: () {
      when(() => catalog.listCategories(market: 'eg'))
          .thenAnswer((_) async => const []);
      return CategoriesListBloc(gateway: catalog);
    },
    act: (b) => b.add(const CategoriesListRequested(market: 'eg')),
    verify: (_) => verify(() => catalog.listCategories(market: 'eg')).called(1),
  );
}
