import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../observability/telemetry_adapter.dart';
import 'secure_token_store.dart';

/// SM-1 from data-model.md.
///
/// States: Guest, Authenticating, Authenticated, Refreshing, RefreshFailed
/// (terminal until re-auth), LoggingOut.
@immutable
sealed class AuthSessionState {
  const AuthSessionState();
}

class AuthGuest extends AuthSessionState {
  const AuthGuest({this.reasonCode});
  final String? reasonCode;
}

class AuthAuthenticating extends AuthSessionState {
  const AuthAuthenticating();
}

class AuthAuthenticated extends AuthSessionState {
  const AuthAuthenticated({
    required this.accessToken,
    required this.refreshToken,
    this.customerId,
    this.email,
    this.displayName,
    this.isVerified = false,
  });

  final String accessToken;
  final String refreshToken;
  final String? customerId;
  final String? email;
  final String? displayName;
  final bool isVerified;

  AuthAuthenticated copyWith({
    String? accessToken,
    String? refreshToken,
    bool? isVerified,
  }) {
    return AuthAuthenticated(
      accessToken: accessToken ?? this.accessToken,
      refreshToken: refreshToken ?? this.refreshToken,
      customerId: customerId,
      email: email,
      displayName: displayName,
      isVerified: isVerified ?? this.isVerified,
    );
  }
}

class AuthRefreshing extends AuthSessionState {
  const AuthRefreshing(this.previous);
  final AuthAuthenticated previous;
}

class AuthRefreshFailed extends AuthSessionState {
  const AuthRefreshFailed();
}

class AuthLoggingOut extends AuthSessionState {
  const AuthLoggingOut();
}

@immutable
sealed class AuthSessionEvent {
  const AuthSessionEvent();
}

class LoginRequested extends AuthSessionEvent {
  const LoginRequested({
    required this.accessToken,
    required this.refreshToken,
    this.customerId,
    this.email,
    this.displayName,
    this.isVerified = false,
  });
  final String accessToken;
  final String refreshToken;
  final String? customerId;
  final String? email;
  final String? displayName;
  final bool isVerified;
}

class LoginAttemptStarted extends AuthSessionEvent {
  const LoginAttemptStarted();
}

class LoginAttemptFailed extends AuthSessionEvent {
  const LoginAttemptFailed({required this.reasonCode});
  final String reasonCode;
}

class RefreshStarted extends AuthSessionEvent {
  const RefreshStarted();
}

class RefreshSucceeded extends AuthSessionEvent {
  const RefreshSucceeded({required this.accessToken, required this.refreshToken});
  final String accessToken;
  final String refreshToken;
}

class RefreshFailed extends AuthSessionEvent {
  const RefreshFailed();
}

class LogoutRequested extends AuthSessionEvent {
  const LogoutRequested();
}

class LogoutCompleted extends AuthSessionEvent {
  const LogoutCompleted();
}

class SessionRehydrated extends AuthSessionEvent {
  const SessionRehydrated({
    required this.accessToken,
    required this.refreshToken,
  });
  final String accessToken;
  final String refreshToken;
}

class AuthSessionBloc extends Bloc<AuthSessionEvent, AuthSessionState> {
  AuthSessionBloc({
    required SecureTokenStore tokenStore,
    TelemetryAdapter? telemetry,
  })  : _tokenStore = tokenStore,
        _telemetry = telemetry ?? const NoopTelemetryAdapter(),
        super(const AuthGuest()) {
    on<LoginAttemptStarted>(_onLoginAttemptStarted);
    on<LoginRequested>(_onLoginRequested);
    on<LoginAttemptFailed>(_onLoginAttemptFailed);
    on<RefreshStarted>(_onRefreshStarted);
    on<RefreshSucceeded>(_onRefreshSucceeded);
    on<RefreshFailed>(_onRefreshFailed);
    on<LogoutRequested>(_onLogoutRequested);
    on<LogoutCompleted>(_onLogoutCompleted);
    on<SessionRehydrated>(_onSessionRehydrated);
  }

  final SecureTokenStore _tokenStore;
  final TelemetryAdapter _telemetry;

  void _onLoginAttemptStarted(
    LoginAttemptStarted event,
    Emitter<AuthSessionState> emit,
  ) {
    if (state is AuthGuest || state is AuthRefreshFailed) {
      emit(const AuthAuthenticating());
    }
  }

  Future<void> _onLoginRequested(
    LoginRequested event,
    Emitter<AuthSessionState> emit,
  ) async {
    await _tokenStore.writeTokens(
      accessToken: event.accessToken,
      refreshToken: event.refreshToken,
    );
    emit(AuthAuthenticated(
      accessToken: event.accessToken,
      refreshToken: event.refreshToken,
      customerId: event.customerId,
      email: event.email,
      displayName: event.displayName,
      isVerified: event.isVerified,
    ));
    _telemetry.emit('auth.login.success');
  }

  void _onLoginAttemptFailed(
    LoginAttemptFailed event,
    Emitter<AuthSessionState> emit,
  ) {
    emit(AuthGuest(reasonCode: event.reasonCode));
    _telemetry.emit(
      'auth.login.failure',
      properties: {'reason_code': event.reasonCode},
    );
  }

  void _onRefreshStarted(
    RefreshStarted event,
    Emitter<AuthSessionState> emit,
  ) {
    final s = state;
    if (s is AuthAuthenticated) {
      emit(AuthRefreshing(s));
    }
  }

  Future<void> _onRefreshSucceeded(
    RefreshSucceeded event,
    Emitter<AuthSessionState> emit,
  ) async {
    final s = state;
    if (s is AuthRefreshing) {
      await _tokenStore.writeTokens(
        accessToken: event.accessToken,
        refreshToken: event.refreshToken,
      );
      emit(s.previous.copyWith(
        accessToken: event.accessToken,
        refreshToken: event.refreshToken,
      ));
    }
  }

  Future<void> _onRefreshFailed(
    RefreshFailed event,
    Emitter<AuthSessionState> emit,
  ) async {
    await _tokenStore.clear();
    emit(const AuthRefreshFailed());
  }

  void _onLogoutRequested(
    LogoutRequested event,
    Emitter<AuthSessionState> emit,
  ) {
    if (state is AuthAuthenticated) {
      emit(const AuthLoggingOut());
    }
  }

  Future<void> _onLogoutCompleted(
    LogoutCompleted event,
    Emitter<AuthSessionState> emit,
  ) async {
    await _tokenStore.clear();
    emit(const AuthGuest());
    _telemetry.emit('more.logout.tapped');
  }

  void _onSessionRehydrated(
    SessionRehydrated event,
    Emitter<AuthSessionState> emit,
  ) {
    if (state is AuthGuest) {
      emit(AuthAuthenticated(
        accessToken: event.accessToken,
        refreshToken: event.refreshToken,
      ));
    }
  }
}
