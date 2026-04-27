import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:customer_flutter/core/observability/telemetry_adapter.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockStorage extends Mock implements FlutterSecureStorage {}

class _CapturingTelemetry extends TelemetryAdapter {
  _CapturingTelemetry();
  final List<({String event, Map<String, Object?> props})> events = [];

  @override
  void emit(String event, {Map<String, Object?> properties = const {}}) {
    events.add((event: event, props: Map<String, Object?>.from(properties)));
  }
}

void main() {
  late _MockStorage storage;
  late _CapturingTelemetry telemetry;

  setUp(() {
    storage = _MockStorage();
    telemetry = _CapturingTelemetry();
    when(() => storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key')))
        .thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
  });

  test('clean install — writes version marker, no migration', () async {
    when(() => storage.read(key: any(named: 'key'))).thenAnswer((_) async => null);
    final store = SecureTokenStore(storage: storage, telemetry: telemetry);

    await store.ensureMigrated();

    verify(() => storage.write(
          key: 'auth.storage_schema_version',
          value: kStorageSchemaVersion.toString(),
        )).called(1);
    expect(telemetry.events, isEmpty);
  });

  test('already on current version — no-op', () async {
    when(() => storage.read(key: 'auth.storage_schema_version'))
        .thenAnswer((_) async => kStorageSchemaVersion.toString());
    final store = SecureTokenStore(storage: storage, telemetry: telemetry);

    await store.ensureMigrated();

    verifyNever(() =>
        storage.write(key: 'auth.storage_schema_version', value: any(named: 'value')));
    expect(telemetry.events, isEmpty);
  });

  test('downgrade detected — wipes and falls back to guest', () async {
    when(() => storage.read(key: 'auth.storage_schema_version'))
        .thenAnswer((_) async => '99'); // future version
    final store = SecureTokenStore(storage: storage, telemetry: telemetry);

    await store.ensureMigrated();

    verify(() => storage.deleteAll()).called(1);
    expect(telemetry.events.single.event, 'auth.storage.migrated');
    expect(telemetry.events.single.props['outcome'], 'success');
  });

  test('corrupt keychain — wipes and emits wiped outcome', () async {
    when(() => storage.read(key: any(named: 'key')))
        .thenThrow(Exception('keychain corrupt'));
    final store = SecureTokenStore(storage: storage, telemetry: telemetry);

    await store.ensureMigrated();

    verify(() => storage.deleteAll()).called(1);
    expect(telemetry.events.single.event, 'auth.storage.migrated');
    expect(telemetry.events.single.props['outcome'], 'wiped');
  });
}
