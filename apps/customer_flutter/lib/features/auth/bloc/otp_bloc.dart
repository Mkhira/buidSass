import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../../core/auth/auth_session_bloc.dart';
import '../data/auth_repository.dart';

const _otpStateKey = 'auth.otp_resend_deadline';

@immutable
class OtpState {
  const OtpState({
    required this.challenge,
    required this.resendInSeconds,
    this.submitting = false,
    this.errorReason,
    this.success = false,
  });

  final OtpChallenge challenge;
  final int resendInSeconds;
  final bool submitting;
  final String? errorReason;
  final bool success;

  OtpState copyWith({
    OtpChallenge? challenge,
    int? resendInSeconds,
    bool? submitting,
    String? errorReason,
    bool? success,
  }) {
    return OtpState(
      challenge: challenge ?? this.challenge,
      resendInSeconds: resendInSeconds ?? this.resendInSeconds,
      submitting: submitting ?? this.submitting,
      errorReason: errorReason,
      success: success ?? this.success,
    );
  }
}

@immutable
sealed class OtpEvent {
  const OtpEvent();
}

class OtpStarted extends OtpEvent {
  const OtpStarted(this.challenge);
  final OtpChallenge challenge;
}

class OtpResendRequested extends OtpEvent {
  const OtpResendRequested();
}

class OtpSubmitted extends OtpEvent {
  const OtpSubmitted(this.code);
  final String code;
}

class _OtpTick extends OtpEvent {
  const _OtpTick();
}

class _OtpRestored extends OtpEvent {
  const _OtpRestored(this.remainingSeconds);
  final int remainingSeconds;
}

/// FR-014 — resend timer is driven by spec 004's response (`retry_after_seconds`)
/// and persisted in secure storage so a tab close / app foregrounding rehydrates
/// the same cooldown.
class OtpBloc extends Bloc<OtpEvent, OtpState> {
  OtpBloc({
    required AuthRepository repository,
    required AuthSessionBloc sessionBloc,
    required OtpChallenge initial,
    FlutterSecureStorage? storage,
  })  : _repository = repository,
        _sessionBloc = sessionBloc,
        _storage = storage ?? const FlutterSecureStorage(),
        super(OtpState(
          challenge: initial,
          resendInSeconds: initial.retryAfterSeconds,
        )) {
    on<OtpStarted>(_onStarted);
    on<OtpResendRequested>(_onResend);
    on<OtpSubmitted>(_onSubmitted);
    on<_OtpTick>(_onTick);
    on<_OtpRestored>(_onRestored);
    _restoreFromStorage();
    _ticker = Stream<void>.periodic(const Duration(seconds: 1)).listen((_) {
      add(const _OtpTick());
    });
  }

  final AuthRepository _repository;
  final AuthSessionBloc _sessionBloc;
  final FlutterSecureStorage _storage;
  late final StreamSubscription<void> _ticker;

  Future<void> _restoreFromStorage() async {
    final raw = await _storage.read(key: _otpStateKey);
    final deadline = int.tryParse(raw ?? '');
    if (deadline == null) return;
    final remaining =
        (deadline - DateTime.now().millisecondsSinceEpoch) ~/ 1000;
    if (remaining > 0) {
      add(_OtpRestored(remaining));
    }
  }

  void _onRestored(_OtpRestored event, Emitter<OtpState> emit) {
    if (event.remainingSeconds > state.resendInSeconds) {
      emit(state.copyWith(resendInSeconds: event.remainingSeconds));
    }
  }

  Future<void> _persistDeadline(int seconds) async {
    final deadline =
        DateTime.now().add(Duration(seconds: seconds)).millisecondsSinceEpoch;
    await _storage.write(key: _otpStateKey, value: deadline.toString());
  }

  void _onStarted(OtpStarted event, Emitter<OtpState> emit) {
    emit(OtpState(
      challenge: event.challenge,
      resendInSeconds: event.challenge.retryAfterSeconds,
    ));
    _persistDeadline(event.challenge.retryAfterSeconds);
  }

  Future<void> _onResend(
    OtpResendRequested event,
    Emitter<OtpState> emit,
  ) async {
    if (state.resendInSeconds > 0) return;
    try {
      final fresh = await _repository.resendOtp(
        challengeId: state.challenge.challengeId,
      );
      emit(state.copyWith(
        challenge: fresh,
        resendInSeconds: fresh.retryAfterSeconds,
      ));
      await _persistDeadline(fresh.retryAfterSeconds);
    } on Object catch (e) {
      emit(state.copyWith(errorReason: e.toString()));
    }
  }

  Future<void> _onSubmitted(
    OtpSubmitted event,
    Emitter<OtpState> emit,
  ) async {
    emit(state.copyWith(submitting: true));
    try {
      final outcome = await _repository.verifyOtp(
        challengeId: state.challenge.challengeId,
        code: event.code,
      );
      if (outcome.ok) {
        _sessionBloc.add(LoginRequested(
          accessToken: outcome.accessToken!,
          refreshToken: outcome.refreshToken!,
          customerId: outcome.customerId,
          email: outcome.email,
          displayName: outcome.displayName,
          isVerified: outcome.isVerified,
        ));
        await _storage.delete(key: _otpStateKey);
        emit(state.copyWith(submitting: false, success: true));
      } else {
        emit(state.copyWith(
          submitting: false,
          errorReason: outcome.reasonCode ?? 'unknown',
        ));
      }
    } on Object catch (e) {
      emit(state.copyWith(submitting: false, errorReason: e.toString()));
    }
  }

  void _onTick(_OtpTick event, Emitter<OtpState> emit) {
    if (state.resendInSeconds > 0) {
      emit(state.copyWith(resendInSeconds: state.resendInSeconds - 1));
    }
  }

  @override
  Future<void> close() async {
    await _ticker.cancel();
    return super.close();
  }
}
