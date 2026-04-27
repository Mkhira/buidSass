import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/auth/auth_session_bloc.dart';
import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:customer_flutter/features/auth/bloc/register_bloc.dart';
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
    when(() => storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key'))).thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
    auth = AuthSessionBloc(tokenStore: SecureTokenStore(storage: storage));
  });

  blocTest<RegisterBloc, RegisterState>(
    'happy path: Submitting -> Success',
    build: () {
      when(() => repo.register(
            email: any(named: 'email'),
            password: any(named: 'password'),
            displayName: any(named: 'displayName'),
          )).thenAnswer((_) async => const AuthOutcome.success(
            accessToken: 'a',
            refreshToken: 'r',
            customerId: 'c',
            email: 'e',
            displayName: 'd',
            isVerified: false,
          ));
      return RegisterBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const RegisterSubmitted(
      email: 'a@b.com',
      password: 'p',
      displayName: 'd',
    )),
    expect: () => [isA<RegisterSubmitting>(), isA<RegisterSuccess>()],
  );

  blocTest<RegisterBloc, RegisterState>(
    'OTP required: Submitting -> RequiresOtp',
    build: () {
      when(() => repo.register(
            email: any(named: 'email'),
            password: any(named: 'password'),
            displayName: any(named: 'displayName'),
          )).thenAnswer((_) async => const AuthOutcome.requiresOtp(
            otpChallenge: OtpChallenge(
              challengeId: 'c',
              channel: 'email',
              retryAfterSeconds: 30,
            ),
          ));
      return RegisterBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const RegisterSubmitted(
      email: 'a@b.com',
      password: 'p',
      displayName: 'd',
    )),
    expect: () => [isA<RegisterSubmitting>(), isA<RegisterRequiresOtp>()],
  );

  blocTest<RegisterBloc, RegisterState>(
    'failure with reason code: Submitting -> Failure(reason)',
    build: () {
      when(() => repo.register(
            email: any(named: 'email'),
            password: any(named: 'password'),
            displayName: any(named: 'displayName'),
          )).thenAnswer((_) async => const AuthOutcome.failure(
            reasonCode: 'identity.email_taken',
          ));
      return RegisterBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const RegisterSubmitted(
      email: 'a@b.com',
      password: 'p',
      displayName: 'd',
    )),
    expect: () => [
      isA<RegisterSubmitting>(),
      isA<RegisterFailure>()
          .having((s) => s.reasonCode, 'reason', 'identity.email_taken'),
    ],
  );

  blocTest<RegisterBloc, RegisterState>(
    'thrown exception: Submitting -> Failure(message)',
    build: () {
      when(() => repo.register(
            email: any(named: 'email'),
            password: any(named: 'password'),
            displayName: any(named: 'displayName'),
          )).thenThrow(Exception('boom'));
      return RegisterBloc(repository: repo, sessionBloc: auth);
    },
    act: (b) => b.add(const RegisterSubmitted(
      email: 'a@b.com',
      password: 'p',
      displayName: 'd',
    )),
    expect: () => [isA<RegisterSubmitting>(), isA<RegisterFailure>()],
  );
}
