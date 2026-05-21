import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class InvitationAcceptEvent {
  const InvitationAcceptEvent();
}

class InvitationAcceptStarted extends InvitationAcceptEvent {
  const InvitationAcceptStarted();
}

class InvitationAccepted extends InvitationAcceptEvent {
  const InvitationAccepted();
}

class InvitationDeclined extends InvitationAcceptEvent {
  const InvitationDeclined();
}

@immutable
sealed class InvitationAcceptState {
  const InvitationAcceptState();
}

class InvitationAcceptValidating extends InvitationAcceptState {
  const InvitationAcceptValidating();
}

class InvitationAcceptReady extends InvitationAcceptState {
  const InvitationAcceptReady({
    this.formError,
    this.submitting = false,
  });
  final String? formError;
  final bool submitting;
}

class InvitationAcceptAccepted extends InvitationAcceptState {
  const InvitationAcceptAccepted(this.result);
  final AcceptInvitationResult result;
}

class InvitationAcceptDeclined extends InvitationAcceptState {
  const InvitationAcceptDeclined();
}

class InvitationAcceptExpired extends InvitationAcceptState {
  const InvitationAcceptExpired();
}

class InvitationAcceptFailure extends InvitationAcceptState {
  const InvitationAcceptFailure();
}

/// Bloc for S-8.11 — deep-link invitation accept/decline.
///
/// Token validation is not surfaced by the OpenAPI as a separate
/// endpoint at v1 — server validates on accept/decline. The bloc
/// exposes a Ready state immediately so the screen can render the
/// accept/decline UI; 410 from either action surfaces as Expired.
class InvitationAcceptBloc
    extends Bloc<InvitationAcceptEvent, InvitationAcceptState> {
  InvitationAcceptBloc({
    required CompaniesGateway gateway,
    required String token,
  })  : _gateway = gateway,
        _token = token,
        super(const InvitationAcceptValidating()) {
    on<InvitationAcceptStarted>(_onStarted);
    on<InvitationAccepted>(_onAccepted);
    on<InvitationDeclined>(_onDeclined);
  }

  final CompaniesGateway _gateway;
  final String _token;

  void _onStarted(
    InvitationAcceptStarted e,
    Emitter<InvitationAcceptState> emit,
  ) {
    if (_token.isEmpty) {
      emit(const InvitationAcceptFailure());
      return;
    }
    emit(const InvitationAcceptReady());
  }

  Future<void> _onAccepted(
    InvitationAccepted e,
    Emitter<InvitationAcceptState> emit,
  ) async {
    final s = state;
    if (s is! InvitationAcceptReady || s.submitting) return;
    emit(const InvitationAcceptReady(submitting: true));
    try {
      final result = await _gateway.acceptInvitation(_token);
      emit(InvitationAcceptAccepted(result));
    } on Object catch (err) {
      final msg = err.toString();
      if (msg.contains('410') || msg.contains('expired')) {
        emit(const InvitationAcceptExpired());
        return;
      }
      emit(const InvitationAcceptReady(
        formError: 'invitation.action_failed',
      ));
    }
  }

  Future<void> _onDeclined(
    InvitationDeclined e,
    Emitter<InvitationAcceptState> emit,
  ) async {
    final s = state;
    if (s is! InvitationAcceptReady || s.submitting) return;
    emit(const InvitationAcceptReady(submitting: true));
    try {
      await _gateway.declineInvitation(_token);
      emit(const InvitationAcceptDeclined());
    } on Object catch (err) {
      final msg = err.toString();
      if (msg.contains('410') || msg.contains('expired')) {
        emit(const InvitationAcceptExpired());
        return;
      }
      emit(const InvitationAcceptReady(
        formError: 'invitation.action_failed',
      ));
    }
  }
}
