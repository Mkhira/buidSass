import 'package:customer_flutter/features/search/data/recent_searches_store.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
  });

  test('push promotes existing query and caps at maxEntries', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () => null,
      maxEntries: 3,
    );

    await store.push('a');
    await store.push('b');
    await store.push('c');
    await store.push('d'); // pushes "a" out
    expect(await store.load(), ['d', 'c', 'b']);

    await store.push('b'); // promotes existing
    expect(await store.load(), ['b', 'd', 'c']);
  });

  test('dedupes case-insensitively', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () => null,
    );

    await store.push('Crown');
    await store.push('crown');
    expect((await store.load()).length, 1);
    expect((await store.load()).single, 'crown');
  });

  test('partitions by account id', () async {
    final prefs = await SharedPreferences.getInstance();
    var accountId = 'alice';
    final store = SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () => accountId,
    );
    await store.push('q-alice');
    accountId = 'bob';
    await store.push('q-bob');
    expect(await store.load(), ['q-bob']);
    accountId = 'alice';
    expect(await store.load(), ['q-alice']);
  });

  test('clear empties the active bucket only', () async {
    final prefs = await SharedPreferences.getInstance();
    var accountId = 'alice';
    final store = SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () => accountId,
    );
    await store.push('a-1');
    accountId = 'bob';
    await store.push('b-1');
    accountId = 'alice';
    await store.clear();
    expect(await store.load(), isEmpty);
    accountId = 'bob';
    expect(await store.load(), ['b-1']);
  });

  test('ignores empty strings', () async {
    final prefs = await SharedPreferences.getInstance();
    final store = SharedPreferencesRecentSearchesStore(
      prefs: prefs,
      accountIdProvider: () => null,
    );
    await store.push('   ');
    await store.push('');
    expect(await store.load(), isEmpty);
  });
}
