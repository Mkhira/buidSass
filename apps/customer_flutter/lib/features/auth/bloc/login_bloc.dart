import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/auth/auth_session_bloc.dart';
import '../data/auth_repository.dart';

@immutable
sealed class LoginState {
  const LoginState();
}

class LoginIdle extends LoginState {
  const LoginIdle();
}

class LoginSubmitting extends LoginState {
  const LoginSubmitting();
}

class LoginRequiresOtp extends LoginState {
  const LoginRequiresOtp(this.challenge);
  final OtpChallenge challenge;
}

class LoginSuccess extends LoginState {
  const LoginSuccess();
}

class LoginFailure extends LoginState {
  const LoginFailure(this.reasonCode);
  final String reasonCode;
}

@immutable
sealed class LoginEvent {
  const LoginEvent();
}

class LoginSubmitted extends LoginEvent {
  const LoginSubmitted({required this.email, required this.password});
  final String email;
  final String password;
}

class LoginBloc extends Bloc<LoginEvent, LoginState> {
  LoginBloc({
    required AuthRepository repository,
    required AuthSessionBloc sessionBloc,
  })  : _repository = repository,
        _sessionBloc = sessionBloc,
        super(const LoginIdle()) {
    on<LoginSubmitted>(_onSubmitted);
  }

  final AuthRepository _repository;
  final AuthSessionBloc _sessionBloc;

  Future<void> _onSubmitted(
    LoginSubmitted event,
    Emitter<LoginState> emit,
  ) async {
    emit(const LoginSubmitting());
    _sessionBloc.add(const LoginAttemptStarted());
    try {
      final outcome = await _repository.login(
        email: event.email,
        password: event.password,
      );
      if (outcome.otpChallenge != null) {
        emit(LoginRequiresOtp(outcome.otpChallenge!));
        return;
      }
      if (outcome.ok) {
        // Guard against a backend contract gap — if `ok` returns without
        // tokens, fail closed instead of force-unwrapping a null.
        final accessToken = outcome.accessToken;
        final refreshToken = outcome.refreshToken;
        if (accessToken == null || refreshToken == null) {
          _sessionBloc
              .add(const LoginAttemptFailed(reasonCode: 'identity.gap'));
          emit(const LoginFailure('identity.gap'));
          return;
        }
        _sessionBloc.add(LoginRequested(
          accessToken: accessToken,
          refreshToken: refreshToken,
          customerId: outcome.customerId,
          email: outcome.email,
          displayName: outcome.displayName,
          isVerified: outcome.isVerified,
        ));
        emit(const LoginSuccess());
      } else {
        _sessionBloc.add(
            LoginAttemptFailed(reasonCode: outcome.reasonCode ?? 'unknown'));
        emit(LoginFailure(outcome.reasonCode ?? 'unknown'));
      }
    } on Object catch (e) {
      _sessionBloc.add(const LoginAttemptFailed(reasonCode: 'identity.gap'));
      emit(LoginFailure(e.toString()));
    }
  }
}
