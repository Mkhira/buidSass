import 'dart:async';
import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// Local recent-searches store (Phase 3 BR-3).
///
/// Behavior:
///
///   * Capped at [maxEntries] (spec.md BR-3: max 10).
///   * Most-recently-used first; pushing an existing query promotes it to
///     the head (LRU semantics — duplicates collapse).
///   * Account-namespaced: signed-in users get a per-account bucket;
///     signed-out users share an anonymous bucket. The account-id is
///     resolved lazily via [accountIdProvider] so the store doesn't need
///     to subscribe to auth state changes.
///   * Backed by `shared_preferences`. Cleared on demand.
abstract class RecentSearchesStore {
  Future<List<String>> load();
  Future<void> push(String query);
  Future<void> clear();
}

/// `shared_preferences`-backed [RecentSearchesStore].
class SharedPreferencesRecentSearchesStore implements RecentSearchesStore {
  SharedPreferencesRecentSearchesStore({
    required this.prefs,
    required this.accountIdProvider,
    this.maxEntries = 10,
  });

  /// Pluggable prefs handle — passed in so tests can swap in
  /// `SharedPreferences.setMockInitialValues`-backed instances.
  final SharedPreferences prefs;

  /// Returns the active account id (signed-in) or `null` for anonymous.
  /// Called per read/write so the bucket follows the auth state without
  /// the store holding its own subscription.
  final String? Function() accountIdProvider;

  final int maxEntries;

  String _key() {
    final id = accountIdProvider();
    return 'search.recent.${id ?? 'anon'}';
  }

  @override
  Future<List<String>> load() async {
    final raw = prefs.getString(_key());
    if (raw == null || raw.isEmpty) return const [];
    try {
      final decoded = json.decode(raw);
      if (decoded is! List) return const [];
      return decoded.whereType<String>().toList(growable: false);
    } on FormatException {
      return const [];
    }
  }

  @override
  Future<void> push(String query) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return;
    final current = await load();
    // Move-to-front + dedupe by case-folded equality so "Crown" and
    // "crown" don't both occupy a slot.
    final lower = trimmed.toLowerCase();
    final remaining =
        current.where((q) => q.toLowerCase() != lower).toList(growable: true);
    remaining.insert(0, trimmed);
    if (remaining.length > maxEntries) {
      remaining.removeRange(maxEntries, remaining.length);
    }
    await prefs.setString(_key(), json.encode(remaining));
  }

  @override
  Future<void> clear() async {
    await prefs.remove(_key());
  }
}

/// In-memory [RecentSearchesStore] used by tests and the offline stub
/// composition. Same semantics as the persistent variant.
class InMemoryRecentSearchesStore implements RecentSearchesStore {
  InMemoryRecentSearchesStore({
    required this.accountIdProvider,
    this.maxEntries = 10,
  });

  final String? Function() accountIdProvider;
  final int maxEntries;
  final Map<String, List<String>> _buckets = {};

  String _key() => accountIdProvider() ?? 'anon';

  @override
  Future<List<String>> load() async =>
      List<String>.unmodifiable(_buckets[_key()] ?? const []);

  @override
  Future<void> push(String query) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return;
    final lower = trimmed.toLowerCase();
    final bucket = List<String>.from(_buckets[_key()] ?? const [])
      ..removeWhere((q) => q.toLowerCase() == lower)
      ..insert(0, trimmed);
    if (bucket.length > maxEntries) {
      bucket.removeRange(maxEntries, bucket.length);
    }
    _buckets[_key()] = bucket;
  }

  @override
  Future<void> clear() async {
    _buckets.remove(_key());
  }
}
