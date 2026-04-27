import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/auth/auth_session_bloc.dart';
import 'package:customer_flutter/core/auth/secure_token_store.dart';
import 'package:customer_flutter/features/auth/bloc/otp_bloc.dart';
import 'package:customer_flutter/features/auth/data/auth_repository.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements AuthRepository {}

class _MockStorage extends Mock implements FlutterSecureStorage {}

const _initial = OtpChallenge(
  challengeId: 'c1',
  channel: 'sms',
  retryAfterSeconds: 30,
);

void main() {
  late _MockRepo repo;
  late AuthSessionBloc auth;
  late _MockStorage storage;

  setUp(() {
    repo = _MockRepo();
    storage = _MockStorage();
    when(() => storage.read(key: any(named: 'key')))
        .thenAnswer((_) async => null);
    when(() => storage.write(key: any(named: 'key'), value: any(named: 'value')))
        .thenAnswer((_) async {});
    when(() => storage.delete(key: any(named: 'key'))).thenAnswer((_) async {});
    when(() => storage.deleteAll()).thenAnswer((_) async {});
    auth = AuthSessionBloc(tokenStore: SecureTokenStore(storage: storage));
  });

  blocTest<OtpBloc, OtpState>(
    'starts with the initial challenge + retry seconds',
    build: () => OtpBloc(
      repository: repo,
      sessionBloc: auth,
      initial: _initial,
      storage: storage,
    ),
    verify: (bloc) {
      expect(bloc.state.challenge.challengeId, 'c1');
      expect(bloc.state.resendInSeconds, 30);
    },
  );

  blocTest<OtpBloc, OtpState>(
    'OtpSubmitted success: bloc transitions to success state',
    build: () {
      when(() => repo.verifyOtp(
            challengeId: any(named: 'challengeId'),
            code: any(named: 'code'),
          )).thenAnswer((_) async => const AuthOutcome.success(
            accessToken: 'a',
            refreshToken: 'r',
            customerId: 'c',
            email: 'e',
            displayName: 'd',
            isVerified: false,
          ));
      return OtpBloc(
        repository: repo,
        sessionBloc: auth,
        initial: _initial,
        storage: storage,
      );
    },
    act: (b) => b.add(const OtpSubmitted('123456')),
    skip: 1, // intermediate submitting state
    expect: () => [
      isA<OtpState>().having((s) => s.success, 'success', true),
    ],
  );

  blocTest<OtpBloc, OtpState>(
    'OtpSubmitted failure: surfaces reason code',
    build: () {
      when(() => repo.verifyOtp(
            challengeId: any(named: 'challengeId'),
            code: any(named: 'code'),
          )).thenAnswer((_) async => const AuthOutcome.failure(
            reasonCode: 'identity.mfa_invalid',
          ));
      return OtpBloc(
        repository: repo,
        sessionBloc: auth,
        initial: _initial,
        storage: storage,
      );
    },
    act: (b) => b.add(const OtpSubmitted('000000')),
    skip: 1,
    expect: () => [
      isA<OtpState>().having(
        (s) => s.errorReason,
        'reason',
        'identity.mfa_invalid',
      ),
    ],
  );

  blocTest<OtpBloc, OtpState>(
    'OtpResendRequested skipped while resendInSeconds > 0',
    build: () => OtpBloc(
      repository: repo,
      sessionBloc: auth,
      initial: _initial,
      storage: storage,
    ),
    act: (b) => b.add(const OtpResendRequested()),
    expect: () => isEmpty,
    verify: (_) {
      verifyNever(() => repo.resendOtp(challengeId: any(named: 'challengeId')));
    },
  );
}
