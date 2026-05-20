import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/search/bloc/search_bloc.dart';
import 'package:customer_flutter/features/search/data/models/search_models.dart';
import 'package:customer_flutter/features/search/data/recent_searches_store.dart';
import 'package:customer_flutter/features/search/data/search_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockGateway extends Mock implements SearchGateway {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      const AutocompleteRequest(query: '', marketCode: '', locale: ''),
    );
    registerFallbackValue(
      const SearchProductsRequest(query: '', marketCode: '', locale: ''),
    );
  });

  late _MockGateway gateway;
  late InMemoryRecentSearchesStore recent;

  SearchBloc build({Duration debounce = const Duration(milliseconds: 1)}) =>
      SearchBloc(
        gateway: gateway,
        recentStore: recent,
        marketProvider: () => 'ksa',
        localeProvider: () => 'en',
        debounce: debounce,
      );

  setUp(() {
    gateway = _MockGateway();
    recent = InMemoryRecentSearchesStore(accountIdProvider: () => null);
  });

  blocTest<SearchBloc, SearchState>(
    'SearchEntered emits Idle with recent loaded',
    setUp: () async {
      await recent.push('crown');
    },
    build: build,
    act: (b) => b.add(const SearchEntered()),
    expect: () => [
      isA<SearchIdle>().having((s) => s.recent, 'recent', ['crown']),
    ],
  );

  blocTest<SearchBloc, SearchState>(
    'SearchQueryChanged debounces then emits Autocompleted',
    build: () {
      when(() => gateway.autocomplete(any())).thenAnswer((_) async {
        return const AutocompleteResult(
          suggestions: [SearchSuggestion(label: 'tile', kind: 'term')],
          topMatches: [],
        );
      });
      return build();
    },
    act: (b) => b.add(const SearchQueryChanged('til')),
    wait: const Duration(milliseconds: 20),
    expect: () => [
      isA<SearchAutocompleting>(),
      isA<SearchAutocompleted>()
          .having((s) => s.suggestions.single.label, 'first label', 'tile'),
    ],
  );

  blocTest<SearchBloc, SearchState>(
    'SearchQueryChanged with empty string returns to Idle',
    setUp: () async {
      await recent.push('previous');
    },
    build: build,
    act: (b) => b.add(const SearchQueryChanged('')),
    wait: const Duration(milliseconds: 20),
    expect: () => [
      isA<SearchIdle>().having((s) => s.recent, 'recent', ['previous']),
    ],
  );

  blocTest<SearchBloc, SearchState>(
    'SearchSubmitted persists recent and emits Results',
    build: () {
      when(() => gateway.searchProducts(any())).thenAnswer((_) async {
        return const SearchProductsResult(
          items: [
            SearchProductItem(
              id: 'p-1',
              slug: 'tile-a',
              name: 'Tile A',
              thumbnailUrl: '',
              priceMinor: 12000,
              currency: 'SAR',
              isRestricted: false,
              inStock: true,
            ),
          ],
          page: 1,
          pageSize: 24,
          totalCount: 1,
          facets: [],
          sortOptions: [],
        );
      });
      return build();
    },
    act: (b) => b.add(const SearchSubmitted('tile')),
    expect: () => [isA<SearchResults>()],
    verify: (_) async {
      expect(await recent.load(), ['tile']);
    },
  );

  blocTest<SearchBloc, SearchState>(
    'Empty results emit SearchEmpty with did-you-mean suggestions',
    build: () {
      when(() => gateway.searchProducts(any())).thenAnswer((_) async {
        return const SearchProductsResult(
          items: [],
          page: 1,
          pageSize: 24,
          totalCount: 0,
          facets: [],
          sortOptions: [],
          suggestions: ['near miss'],
        );
      });
      return build();
    },
    act: (b) => b.add(const SearchSubmitted('qwertyz')),
    expect: () => [
      isA<SearchEmpty>()
          .having((s) => s.suggestions, 'suggestions', ['near miss']),
    ],
  );

  blocTest<SearchBloc, SearchState>(
    'SearchFacetToggled refetches with new facet selection',
    build: () {
      when(() => gateway.searchProducts(any())).thenAnswer((invocation) async {
        return const SearchProductsResult(
          items: [
            SearchProductItem(
              id: 'p-1',
              slug: 'tile-a',
              name: 'Tile A',
              thumbnailUrl: '',
              priceMinor: 12000,
              currency: 'SAR',
              isRestricted: false,
              inStock: true,
            ),
          ],
          page: 1,
          pageSize: 24,
          totalCount: 1,
          facets: [],
          sortOptions: [],
        );
      });
      return build();
    },
    seed: () => const SearchResults(
      query: 'tile',
      items: [],
      facets: [],
      sortOptions: [],
      selectedFacets: {},
      page: 1,
      pageSize: 24,
      totalCount: 0,
    ),
    act: (b) =>
        b.add(const SearchFacetToggled(kind: 'brand', value: 'brand-x')),
    expect: () => [isA<SearchResults>()],
    verify: (_) {
      final capture =
          verify(() => gateway.searchProducts(captureAny())).captured.last
              as SearchProductsRequest;
      expect(capture.facets['brand'], ['brand-x']);
    },
  );

  blocTest<SearchBloc, SearchState>(
    'SearchPageRequested appends items when hasMore',
    build: () {
      when(() => gateway.searchProducts(any())).thenAnswer((_) async {
        return const SearchProductsResult(
          items: [
            SearchProductItem(
              id: 'p-2',
              slug: 'tile-b',
              name: 'Tile B',
              thumbnailUrl: '',
              priceMinor: 13000,
              currency: 'SAR',
              isRestricted: false,
              inStock: true,
            ),
          ],
          page: 2,
          pageSize: 1,
          totalCount: 2,
          facets: [],
          sortOptions: [],
        );
      });
      return build();
    },
    seed: () => const SearchResults(
      query: 'tile',
      items: [
        SearchProductItem(
          id: 'p-1',
          slug: 'tile-a',
          name: 'Tile A',
          thumbnailUrl: '',
          priceMinor: 12000,
          currency: 'SAR',
          isRestricted: false,
          inStock: true,
        ),
      ],
      facets: [],
      sortOptions: [],
      selectedFacets: {},
      page: 1,
      pageSize: 1,
      totalCount: 2,
    ),
    act: (b) => b.add(const SearchPageRequested()),
    expect: () => [
      isA<SearchResults>().having((s) => s.isLoadingMore, 'loading', true),
      isA<SearchResults>().having((s) => s.items.length, 'count', 2),
    ],
  );

  blocTest<SearchBloc, SearchState>(
    'SearchRecentCleared empties the recent bucket',
    setUp: () async {
      await recent.push('crown');
      await recent.push('bracket');
    },
    build: build,
    seed: () => const SearchIdle(recent: ['crown', 'bracket']),
    act: (b) => b.add(const SearchRecentCleared()),
    expect: () => [
      isA<SearchIdle>().having((s) => s.recent, 'recent', isEmpty),
    ],
    verify: (_) async {
      expect(await recent.load(), isEmpty);
    },
  );
}
