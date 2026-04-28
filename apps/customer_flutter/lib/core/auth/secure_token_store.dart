import 'dart:async';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../observability/telemetry_adapter.dart';

const _kAccessToken = 'auth.access_token';
const _kRefreshToken = 'auth.refresh_token';
const _kSchemaVersion = 'auth.storage_schema_version';

/// Current schema version for tokens kept in `flutter_secure_storage`.
/// Bump when the on-disk shape changes; add a branch to
/// [SecureTokenStore._migrateFromVersion].
const int kStorageSchemaVersion = 1;

/// SecureTokenStore — wraps [FlutterSecureStorage] for refresh + access
/// tokens with versioned-schema migration (FR-015a). On migration failure
/// the store wipes itself and lands on a clean guest session.
class SecureTokenStore {
  SecureTokenStore({
    FlutterSecureStorage? storage,
    TelemetryAdapter? telemetry,
  })  : _storage = storage ?? const FlutterSecureStorage(),
        _telemetry = telemetry ?? const NoopTelemetryAdapter();

  final FlutterSecureStorage _storage;
  final TelemetryAdapter _telemetry;

  Future<void>? _migrationFuture;

  Future<void> ensureMigrated() {
    return _migrationFuture ??= _migrate();
  }

  Future<void> _migrate() async {
    try {
      final raw = await _storage.read(key: _kSchemaVersion);
      final from = int.tryParse(raw ?? '');
      if (from == kStorageSchemaVersion) {
        return;
      }
      if (from == null) {
        // Clean install (or pre-migration) — write the version marker.
        await _storage.write(
          key: _kSchemaVersion,
          value: kStorageSchemaVersion.toString(),
        );
        return;
      }
      await _migrateFromVersion(from);
      await _storage.write(
        key: _kSchemaVersion,
        value: kStorageSchemaVersion.toString(),
      );
      _telemetry.emit(
        'auth.storage.migrated',
        properties: {
          'from_version': from,
          'to_version': kStorageSchemaVersion,
          'outcome': 'success',
        },
      );
    } on Object {
      try {
        await _storage.deleteAll();
      } on Object {
        // best-effort wipe
      }
      _telemetry.emit(
        'auth.storage.migrated',
        properties: const {
          'from_version': null,
          'to_version': kStorageSchemaVersion,
          'outcome': 'wiped',
        },
      );
    }
  }

  /// Hook for future schema migrations. Today the only known shape is v1, so
  /// any other observed version triggers a wipe-and-fallback path.
  Future<void> _migrateFromVersion(int from) async {
    if (from > kStorageSchemaVersion) {
      // Downgrade is not supported — wipe and treat as guest.
      await _storage.deleteAll();
    }
    // (Future v1 -> v2 migrations land here.)
  }

  Future<String?> readAccessToken() async {
    await ensureMigrated();
    return _storage.read(key: _kAccessToken);
  }

  Future<String?> readRefreshToken() async {
    await ensureMigrated();
    return _storage.read(key: _kRefreshToken);
  }

  Future<void> writeTokens({
    required String accessToken,
    required String refreshToken,
  }) async {
    await ensureMigrated();
    await Future.wait([
      _storage.write(key: _kAccessToken, value: accessToken),
      _storage.write(key: _kRefreshToken, value: refreshToken),
    ]);
  }

  Future<void> clear() async {
    await Future.wait([
      _storage.delete(key: _kAccessToken),
      _storage.delete(key: _kRefreshToken),
    ]);
  }
}
