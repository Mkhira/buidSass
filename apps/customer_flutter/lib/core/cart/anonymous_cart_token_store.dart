import 'dart:async';
import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

const _kAnonymousCartToken = 'cart.anonymous_token';

/// AnonymousCartTokenStore — persists / rotates the guest cart token in
/// secure storage. The auth interceptor pipeline (or spec 009 cart client)
/// consumes the token via [readToken].
class AnonymousCartTokenStore {
  AnonymousCartTokenStore({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  Future<String> readOrCreateToken() async {
    final existing = await _storage.read(key: _kAnonymousCartToken);
    if (existing != null && existing.isNotEmpty) return existing;
    final created = _generateToken();
    await _storage.write(key: _kAnonymousCartToken, value: created);
    return created;
  }

  Future<String?> readToken() => _storage.read(key: _kAnonymousCartToken);

  Future<void> rotate() async {
    await _storage.delete(key: _kAnonymousCartToken);
  }

  Future<void> clear() => _storage.delete(key: _kAnonymousCartToken);

  static String _generateToken() {
    final r = Random.secure();
    final bytes = List<int>.generate(24, (_) => r.nextInt(256));
    return bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  }
}
