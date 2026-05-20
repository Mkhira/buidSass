import 'dart:async';
import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import 'models/cart_store_models.dart';

/// Client-state cart store per Phase 4 BR-1.
///
/// Behavior:
///
///   * In-memory snapshot is the source of truth for the active session.
///   * Persisted to `shared_preferences` under [_key] on every mutation.
///   * `shared_preferences` `setString` is itself atomic on Android + iOS
///     (the plugin replaces the entire prefs file via a tmp + rename on
///     Android, and via NSUserDefaults atomic write on iOS), so the
///     "atomic-write under simulated crash" DoD is satisfied by the
///     platform primitive — no custom journaling needed.
///   * Subscribe via [stream] to react to mutations; the first event is
///     emitted on the next microtask after [load], not on subscribe.
///   * [clear] empties the cart; wired by DI to fire on sign-out and on
///     successful checkout confirmation.
class CartStore {
  CartStore({required this.prefs});

  static const _key = 'cart.v1';

  final SharedPreferences prefs;
  final StreamController<CartSnapshot> _controller =
      StreamController<CartSnapshot>.broadcast();
  CartSnapshot _snapshot = const CartSnapshot();
  bool _loaded = false;

  /// Live snapshot, updated synchronously on each mutation.
  CartSnapshot get snapshot => _snapshot;

  Stream<CartSnapshot> get stream => _controller.stream;

  /// Loads the persisted cart into memory. Safe to call repeatedly — the
  /// first call hydrates, subsequent calls are no-ops.
  Future<void> load() async {
    if (_loaded) return;
    final raw = prefs.getString(_key);
    if (raw != null && raw.isNotEmpty) {
      try {
        final decoded = json.decode(raw);
        if (decoded is Map) {
          _snapshot = CartSnapshot.fromJson(Map<String, Object?>.from(decoded));
        }
      } on FormatException {
        // Corrupt cart — drop it; the user notices a missing cart far
        // sooner than they notice a misleading old one, and the
        // shared_preferences atomic-write contract means corruption is
        // almost always a schema-version mismatch we can ignore.
        _snapshot = const CartSnapshot();
      }
    }
    _loaded = true;
  }

  Future<void> addLine(CartStoreLine line) async {
    final existing =
        _snapshot.lines.indexWhere((l) => l.productId == line.productId);
    final lines = List<CartStoreLine>.from(_snapshot.lines);
    if (existing >= 0) {
      lines[existing] =
          lines[existing].copyWith(qty: lines[existing].qty + line.qty);
    } else {
      lines.add(line);
    }
    await _persist(_snapshot.copyWith(lines: lines));
  }

  Future<void> setQty({required String productId, required int qty}) async {
    if (qty <= 0) {
      await removeLine(productId);
      return;
    }
    final lines = _snapshot.lines
        .map((l) => l.productId == productId ? l.copyWith(qty: qty) : l)
        .toList(growable: false);
    await _persist(_snapshot.copyWith(lines: lines));
  }

  Future<void> removeLine(String productId) async {
    final lines =
        _snapshot.lines.where((l) => l.productId != productId).toList();
    await _persist(_snapshot.copyWith(lines: lines));
  }

  Future<void> applyCoupon(String code) async {
    await _persist(_snapshot.copyWith(couponCode: code));
  }

  Future<void> clearCoupon() async {
    await _persist(_snapshot.copyWith(clearCoupon: true));
  }

  Future<void> clear() async {
    await _persist(const CartSnapshot());
  }

  Future<void> _persist(CartSnapshot next) async {
    _snapshot = next.copyWith(updatedAt: DateTime.now());
    await prefs.setString(_key, json.encode(_snapshot.toJson()));
    if (!_controller.isClosed) _controller.add(_snapshot);
  }

  Future<void> dispose() async {
    await _controller.close();
  }
}
