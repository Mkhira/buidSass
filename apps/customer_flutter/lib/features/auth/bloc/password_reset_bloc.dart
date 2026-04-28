import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/auth_repository.dart';

@immutable
sealed class PasswordResetState {
  const PasswordResetState();
}

class PasswordResetIdle extends PasswordResetState {
  const PasswordResetIdle();
}

class PasswordResetSubmitting extends PasswordResetState {
  const PasswordResetSubmitting();
}

class PasswordResetEmailSent extends PasswordResetState {
  const PasswordResetEmailSent();
}

class PasswordResetConfirmed extends PasswordResetState {
  const PasswordResetConfirmed();
}

class PasswordResetFailure extends PasswordResetState {
  const PasswordResetFailure(this.reasonCode);
  final String reasonCode;
}

@immutable
sealed class PasswordResetEvent {
  const PasswordResetEvent();
}

class PasswordResetEmailRequested extends PasswordResetEvent {
  const PasswordResetEmailRequested(this.email);
  final String email;
}

class PasswordResetConfirmSubmitted extends PasswordResetEvent {
  const PasswordResetConfirmSubmitted({
    required this.token,
    required this.newPassword,
  });
  final String token;
  final String newPassword;
}

class PasswordResetBloc extends Bloc<PasswordResetEvent, PasswordResetState> {
  PasswordResetBloc({required AuthRepository repository})
      : _repository = repository,
        super(const PasswordResetIdle()) {
    on<PasswordResetEmailRequested>(_onRequested);
    on<PasswordResetConfirmSubmitted>(_onConfirmed);
  }

  final AuthRepository _repository;

  Future<void> _onRequested(
    PasswordResetEmailRequested event,
    Emitter<PasswordResetState> emit,
  ) async {
    emit(const PasswordResetSubmitting());
    try {
      await _repository.requestPasswordReset(email: event.email);
      emit(const PasswordResetEmailSent());
    } on Object catch (e) {
      emit(PasswordResetFailure(e.toString()));
    }
  }

  Future<void> _onConfirmed(
    PasswordResetConfirmSubmitted event,
    Emitter<PasswordResetState> emit,
  ) async {
    emit(const PasswordResetSubmitting());
    try {
      await _repository.confirmPasswordReset(
        token: event.token,
        newPassword: event.newPassword,
      );
      emit(const PasswordResetConfirmed());
    } on Object catch (e) {
      emit(PasswordResetFailure(e.toString()));
    }
  }
}
