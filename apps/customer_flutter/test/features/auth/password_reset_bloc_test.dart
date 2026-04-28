import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/auth/bloc/password_reset_bloc.dart';
import 'package:customer_flutter/features/auth/data/auth_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements AuthRepository {}

void main() {
  late _MockRepo repo;
  setUp(() => repo = _MockRepo());

  blocTest<PasswordResetBloc, PasswordResetState>(
    'EmailRequested success: Submitting -> EmailSent',
    build: () {
      when(() => repo.requestPasswordReset(email: any(named: 'email')))
          .thenAnswer((_) async {});
      return PasswordResetBloc(repository: repo);
    },
    act: (b) => b.add(const PasswordResetEmailRequested('a@b.com')),
    expect: () => [
      isA<PasswordResetSubmitting>(),
      isA<PasswordResetEmailSent>(),
    ],
  );

  blocTest<PasswordResetBloc, PasswordResetState>(
    'EmailRequested failure: Submitting -> Failure',
    build: () {
      when(() => repo.requestPasswordReset(email: any(named: 'email')))
          .thenThrow(Exception('rate-limited'));
      return PasswordResetBloc(repository: repo);
    },
    act: (b) => b.add(const PasswordResetEmailRequested('a@b.com')),
    expect: () => [
      isA<PasswordResetSubmitting>(),
      isA<PasswordResetFailure>(),
    ],
  );

  blocTest<PasswordResetBloc, PasswordResetState>(
    'ConfirmSubmitted success: Submitting -> Confirmed',
    build: () {
      when(() => repo.confirmPasswordReset(
            token: any(named: 'token'),
            newPassword: any(named: 'newPassword'),
          )).thenAnswer((_) async {});
      return PasswordResetBloc(repository: repo);
    },
    act: (b) => b.add(const PasswordResetConfirmSubmitted(
      token: 't',
      newPassword: 'p',
    )),
    expect: () => [
      isA<PasswordResetSubmitting>(),
      isA<PasswordResetConfirmed>(),
    ],
  );

  blocTest<PasswordResetBloc, PasswordResetState>(
    'ConfirmSubmitted failure: Submitting -> Failure',
    build: () {
      when(() => repo.confirmPasswordReset(
            token: any(named: 'token'),
            newPassword: any(named: 'newPassword'),
          )).thenThrow(Exception('expired-token'));
      return PasswordResetBloc(repository: repo);
    },
    act: (b) => b.add(const PasswordResetConfirmSubmitted(
      token: 't',
      newPassword: 'p',
    )),
    expect: () => [
      isA<PasswordResetSubmitting>(),
      isA<PasswordResetFailure>(),
    ],
  );
}
