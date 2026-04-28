import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/auth/auth_session_bloc.dart';
import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:customer_flutter/features/auth/bloc/login_bloc.dart';
import 'package:customer_flutter/features/auth/data/auth_repository.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements AuthRepository {}

class _MockStorage extends Mock implements FlutterSecureStorage {}

void main() {
  late _MockRepo repo;
  late AuthSessionBloc auth;

  setUp(() {
    repo = _MockRepo();
    final storage = _MockStorage();
    when(() => storage.read(key: any(named: 'key')))
        .thenAnswer((_) async => null);
    when(() =>
            storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key'))).thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
    auth = AuthSessionBloc(tokenStore: SecureTokenStore(storage: storage));
  });

  blocTest<LoginBloc, LoginState>(
    'happy path emits Submitting -> Success',
    build: () {
      when(() => repo.login(
            email: any(named: 'email'),
            password: any(named: 'password'),
          )).thenAnswer((_) async => const AuthOutcome.success(
            accessToken: 'a',
            refreshToken: 'r',
            customerId: 'c',
            email: 'e',
            displayName: 'd',
            isVerified: false,
          ));
      return LoginBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const LoginSubmitted(email: 'a@b.com', password: 'p')),
    expect: () => [isA<LoginSubmitting>(), isA<LoginSuccess>()],
  );

  blocTest<LoginBloc, LoginState>(
    'OTP required emits Submitting -> RequiresOtp',
    build: () {
      when(() => repo.login(
            email: any(named: 'email'),
            password: any(named: 'password'),
          )).thenAnswer((_) async => const AuthOutcome.requiresOtp(
            otpChallenge: OtpChallenge(
              challengeId: 'c',
              channel: 'sms',
              retryAfterSeconds: 30,
            ),
          ));
      return LoginBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const LoginSubmitted(email: 'a@b.com', password: 'p')),
    expect: () => [isA<LoginSubmitting>(), isA<LoginRequiresOtp>()],
  );

  blocTest<LoginBloc, LoginState>(
    'failure emits Submitting -> Failure',
    build: () {
      when(() => repo.login(
            email: any(named: 'email'),
            password: any(named: 'password'),
          )).thenAnswer((_) async => const AuthOutcome.failure(
            reasonCode: 'identity.invalid_credentials',
          ));
      return LoginBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const LoginSubmitted(email: 'a@b.com', password: 'p')),
    expect: () => [isA<LoginSubmitting>(), isA<LoginFailure>()],
  );
}
