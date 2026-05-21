import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class InviteUserEvent {
  const InviteUserEvent();
}

class InviteUserEmailChanged extends InviteUserEvent {
  const InviteUserEmailChanged(this.value);
  final String value;
}

class InviteUserRoleChanged extends InviteUserEvent {
  const InviteUserRoleChanged(this.value);
  final String value;
}

class InviteUserSubmitted extends InviteUserEvent {
  const InviteUserSubmitted();
}

@immutable
sealed class InviteUserState {
  const InviteUserState();
}

class InviteUserForm extends InviteUserState {
  const InviteUserForm({
    required this.email,
    required this.role,
    this.formError,
  });

  final String email;
  final String role;
  final String? formError;

  bool get canSubmit => email.trim().contains('@') && role.isNotEmpty;

  InviteUserForm copyWith({
    String? email,
    String? role,
    Object? formError = _sentinel,
  }) {
    return InviteUserForm(
      email: email ?? this.email,
      role: role ?? this.role,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class InviteUserSubmitting extends InviteUserState {
  const InviteUserSubmitting(this.form);
  final InviteUserForm form;
}

class InviteUserDone extends InviteUserState {
  const InviteUserDone(this.result);
  final CreateInvitationResult result;
}

const _sentinel = Object();

class InviteUserBloc extends Bloc<InviteUserEvent, InviteUserState> {
  InviteUserBloc({
    required CompaniesGateway gateway,
    required String companyId,
  })  : _gateway = gateway,
        _companyId = companyId,
        super(const InviteUserForm(email: '', role: 'buyer')) {
    on<InviteUserEmailChanged>(_onEmail);
    on<InviteUserRoleChanged>(_onRole);
    on<InviteUserSubmitted>(_onSubmitted);
  }

  final CompaniesGateway _gateway;
  final String _companyId;

  void _onEmail(InviteUserEmailChanged e, Emitter<InviteUserState> emit) {
    final s = state;
    if (s is! InviteUserForm) return;
    emit(s.copyWith(email: e.value));
  }

  void _onRole(InviteUserRoleChanged e, Emitter<InviteUserState> emit) {
    final s = state;
    if (s is! InviteUserForm) return;
    emit(s.copyWith(role: e.value));
  }

  Future<void> _onSubmitted(
    InviteUserSubmitted e,
    Emitter<InviteUserState> emit,
  ) async {
    final s = state;
    if (s is! InviteUserForm || !s.canSubmit) return;
    emit(InviteUserSubmitting(s));
    try {
      final result = await _gateway.invite(
        companyId: _companyId,
        request: CreateInvitationRequest(
          email: s.email.trim(),
          role: s.role,
        ),
      );
      emit(InviteUserDone(result));
    } on Object catch (_) {
      emit(s.copyWith(formError: 'invitation.send_failed'));
    }
  }
}
