import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/catalog/bloc/listing_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_repository.dart';
import 'package:customer_flutter/features/catalog/data/catalog_view_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements CatalogRepository {}

void main() {
  late _MockRepo repo;
  setUp(() => repo = _MockRepo());

  blocTest<ListingBloc, ListingState>(
    'QueryChanged emits Loading -> Loaded after debounce',
    build: () {
      when(() => repo.fetchListing(
            query: any(named: 'query'),
            categoryId: any(named: 'categoryId'),
            sortKey: any(named: 'sortKey'),
            selectedFacets: any(named: 'selectedFacets'),
            cursor: any(named: 'cursor'),
          )).thenAnswer((_) async => const ListingPage(
            items: [
              ProductListingItem(
                id: '1',
                name: 'A',
                thumbnailUrl: '',
                priceMinor: 100,
                currency: 'SAR',
                isRestricted: false,
                inStock: true,
              )
            ],
            facets: [],
            nextCursor: null,
          ));
      return ListingBloc(repository: repo);
    },
    act: (b) => b.add(const QueryChanged('foo')),
    wait: const Duration(milliseconds: 300),
    expect: () => [isA<ListingLoading>(), isA<ListingLoaded>()],
  );

  blocTest<ListingBloc, ListingState>(
    'SortChanged refetches',
    build: () {
      when(() => repo.fetchListing(
                query: any(named: 'query'),
                categoryId: any(named: 'categoryId'),
                sortKey: any(named: 'sortKey'),
                selectedFacets: any(named: 'selectedFacets'),
                cursor: any(named: 'cursor'),
              ))
          .thenAnswer((_) async =>
              const ListingPage(items: [], facets: [], nextCursor: null));
      return ListingBloc(repository: repo);
    },
    act: (b) => b.add(const SortChanged('priceAsc')),
    expect: () => [isA<ListingLoading>(), isA<ListingEmpty>()],
  );

  blocTest<ListingBloc, ListingState>(
    'fetch failure -> ListingError',
    build: () {
      when(() => repo.fetchListing(
            query: any(named: 'query'),
            categoryId: any(named: 'categoryId'),
            sortKey: any(named: 'sortKey'),
            selectedFacets: any(named: 'selectedFacets'),
            cursor: any(named: 'cursor'),
          )).thenThrow(Exception('boom'));
      return ListingBloc(repository: repo);
    },
    act: (b) => b.add(const SortChanged('priceAsc')),
    expect: () => [isA<ListingLoading>(), isA<ListingError>()],
  );
}
