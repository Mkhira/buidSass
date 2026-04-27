import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/auth/auth_session_bloc.dart';
import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockStorage extends Mock implements FlutterSecureStorage {}

void main() {
  late _MockStorage storage;
  late SecureTokenStore tokenStore;

  setUp(() {
    storage = _MockStorage();
    when(() => storage.read(key: any(named: 'key')))
        .thenAnswer((_) async => null);
    when(() =>
            storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key'))).thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
    tokenStore = SecureTokenStore(storage: storage);
  });

  AuthSessionBloc build() => AuthSessionBloc(tokenStore: tokenStore);

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Guest -> Authenticating on LoginAttemptStarted',
    build: build,
    act: (b) => b.add(const LoginAttemptStarted()),
    expect: () => [isA<AuthAuthenticating>()],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Authenticating -> Authenticated on LoginRequested',
    build: build,
    seed: () => const AuthAuthenticating(),
    act: (b) => b.add(const LoginRequested(
      accessToken: 'a',
      refreshToken: 'r',
      customerId: 'c1',
    )),
    expect: () => [
      isA<AuthAuthenticated>()
          .having((s) => s.accessToken, 'access', 'a')
          .having((s) => s.refreshToken, 'refresh', 'r')
          .having((s) => s.customerId, 'customerId', 'c1'),
    ],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Authenticating -> Guest on LoginAttemptFailed',
    build: build,
    seed: () => const AuthAuthenticating(),
    act: (b) => b.add(
        const LoginAttemptFailed(reasonCode: 'identity.invalid_credentials')),
    expect: () => [
      isA<AuthGuest>().having(
          (s) => s.reasonCode, 'reason', 'identity.invalid_credentials'),
    ],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Authenticated -> Refreshing -> Authenticated on Refresh success',
    build: build,
    seed: () => const AuthAuthenticated(accessToken: 'a', refreshToken: 'r'),
    act: (b) => b
      ..add(const RefreshStarted())
      ..add(const RefreshSucceeded(accessToken: 'a2', refreshToken: 'r2')),
    expect: () => [
      isA<AuthRefreshing>(),
      isA<AuthAuthenticated>()
          .having((s) => s.accessToken, 'access', 'a2')
          .having((s) => s.refreshToken, 'refresh', 'r2'),
    ],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Refreshing -> RefreshFailed on RefreshFailed',
    build: build,
    seed: () => const AuthRefreshing(
      AuthAuthenticated(accessToken: 'a', refreshToken: 'r'),
    ),
    act: (b) => b.add(const RefreshFailed()),
    expect: () => [isA<AuthRefreshFailed>()],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'RefreshFailed -> Authenticating on LoginAttemptStarted',
    build: build,
    seed: () => const AuthRefreshFailed(),
    act: (b) => b.add(const LoginAttemptStarted()),
    expect: () => [isA<AuthAuthenticating>()],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Authenticated -> LoggingOut -> Guest',
    build: build,
    seed: () => const AuthAuthenticated(accessToken: 'a', refreshToken: 'r'),
    act: (b) => b
      ..add(const LogoutRequested())
      ..add(const LogoutCompleted()),
    expect: () => [
      isA<AuthLoggingOut>(),
      isA<AuthGuest>(),
    ],
  );

  blocTest<AuthSessionBloc, AuthSessionState>(
    'Guest -> Authenticated on SessionRehydrated',
    build: build,
    act: (b) =>
        b.add(const SessionRehydrated(accessToken: 'a', refreshToken: 'r')),
    expect: () => [
      isA<AuthAuthenticated>()
          .having((s) => s.accessToken, 'access', 'a')
          .having((s) => s.refreshToken, 'refresh', 'r'),
    ],
  );
}
