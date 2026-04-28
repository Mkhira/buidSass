import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/auth/auth_session_bloc.dart';
import '../data/auth_repository.dart';

@immutable
sealed class RegisterState {
  const RegisterState();
}

class RegisterIdle extends RegisterState {
  const RegisterIdle();
}

class RegisterSubmitting extends RegisterState {
  const RegisterSubmitting();
}

class RegisterRequiresOtp extends RegisterState {
  const RegisterRequiresOtp(this.challenge);
  final OtpChallenge challenge;
}

class RegisterSuccess extends RegisterState {
  const RegisterSuccess();
}

class RegisterFailure extends RegisterState {
  const RegisterFailure(this.reasonCode);
  final String reasonCode;
}

@immutable
sealed class RegisterEvent {
  const RegisterEvent();
}

class RegisterSubmitted extends RegisterEvent {
  const RegisterSubmitted({
    required this.email,
    required this.password,
    required this.displayName,
  });
  final String email;
  final String password;
  final String displayName;
}

class RegisterBloc extends Bloc<RegisterEvent, RegisterState> {
  RegisterBloc({
    required AuthRepository repository,
    required AuthSessionBloc sessionBloc,
  })  : _repository = repository,
        _sessionBloc = sessionBloc,
        super(const RegisterIdle()) {
    on<RegisterSubmitted>(_onSubmitted);
  }

  final AuthRepository _repository;
  final AuthSessionBloc _sessionBloc;

  Future<void> _onSubmitted(
    RegisterSubmitted event,
    Emitter<RegisterState> emit,
  ) async {
    emit(const RegisterSubmitting());
    try {
      final outcome = await _repository.register(
        email: event.email,
        password: event.password,
        displayName: event.displayName,
      );
      if (outcome.otpChallenge != null) {
        emit(RegisterRequiresOtp(outcome.otpChallenge!));
        return;
      }
      if (outcome.ok) {
        _sessionBloc.add(LoginRequested(
          accessToken: outcome.accessToken!,
          refreshToken: outcome.refreshToken!,
          customerId: outcome.customerId,
          email: outcome.email,
          displayName: outcome.displayName,
          isVerified: outcome.isVerified,
        ));
        emit(const RegisterSuccess());
      } else {
        emit(RegisterFailure(outcome.reasonCode ?? 'unknown'));
      }
    } on Object catch (e) {
      emit(RegisterFailure(e.toString()));
    }
  }
}
