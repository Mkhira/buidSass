import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class MembershipsEvent {
  const MembershipsEvent();
}

class MembershipsStarted extends MembershipsEvent {
  const MembershipsStarted();
}

class MembershipsRefreshed extends MembershipsEvent {
  const MembershipsRefreshed();
}

class MembershipsRoleChanged extends MembershipsEvent {
  const MembershipsRoleChanged({
    required this.membershipId,
    required this.role,
  });
  final String membershipId;
  final String role;
}

class MembershipsRemoveRequested extends MembershipsEvent {
  const MembershipsRemoveRequested(this.membershipId);
  final String membershipId;
}

@immutable
sealed class MembershipsState {
  const MembershipsState();
}

class MembershipsLoading extends MembershipsState {
  const MembershipsLoading();
}

class MembershipsLoadFailure extends MembershipsState {
  const MembershipsLoadFailure({required this.reason});
  final String reason;
}

class MembershipsLoaded extends MembershipsState {
  const MembershipsLoaded({
    required this.company,
    this.busyMembershipId,
    this.actionError,
  });

  final Company company;
  final String? busyMembershipId;
  final String? actionError;

  MembershipsLoaded copyWith({
    Company? company,
    Object? busyMembershipId = _sentinel,
    Object? actionError = _sentinel,
  }) {
    return MembershipsLoaded(
      company: company ?? this.company,
      busyMembershipId: identical(busyMembershipId, _sentinel)
          ? this.busyMembershipId
          : busyMembershipId as String?,
      actionError: identical(actionError, _sentinel)
          ? this.actionError
          : actionError as String?,
    );
  }
}

const _sentinel = Object();

/// Bloc for S-8.12 — memberships role change + remove. Admin-only
/// (BR-6); server enforces. The bloc defensively blocks demoting the
/// last admin from the client side too.
class MembershipsBloc extends Bloc<MembershipsEvent, MembershipsState> {
  MembershipsBloc({
    required CompaniesGateway gateway,
    required String companyId,
  })  : _gateway = gateway,
        _companyId = companyId,
        super(const MembershipsLoading()) {
    on<MembershipsStarted>(_load);
    on<MembershipsRefreshed>(_load);
    on<MembershipsRoleChanged>(_onRoleChanged);
    on<MembershipsRemoveRequested>(_onRemove);
  }

  final CompaniesGateway _gateway;
  final String _companyId;

  Future<void> _load(
    MembershipsEvent e,
    Emitter<MembershipsState> emit,
  ) async {
    if (state is! MembershipsLoaded) emit(const MembershipsLoading());
    try {
      final company = await _gateway.getById(_companyId);
      emit(MembershipsLoaded(company: company));
    } on Object catch (_) {
      emit(const MembershipsLoadFailure(reason: 'company.load_failed'));
    }
  }

  bool _wouldDemoteLastAdmin(
    Company company,
    String membershipId,
    String newRole,
  ) {
    final admins = company.memberships
        .where((m) => m.role == 'admin')
        .toList(growable: false);
    if (admins.length != 1) return false;
    return admins.single.id == membershipId && newRole != 'admin';
  }

  bool _wouldRemoveLastAdmin(Company company, String membershipId) {
    final admins = company.memberships
        .where((m) => m.role == 'admin')
        .toList(growable: false);
    if (admins.length != 1) return false;
    return admins.single.id == membershipId;
  }

  Future<void> _onRoleChanged(
    MembershipsRoleChanged e,
    Emitter<MembershipsState> emit,
  ) async {
    final s = state;
    if (s is! MembershipsLoaded || s.busyMembershipId != null) return;
    if (_wouldDemoteLastAdmin(s.company, e.membershipId, e.role)) {
      emit(s.copyWith(actionError: 'memberships.last_admin_protected'));
      return;
    }
    emit(s.copyWith(busyMembershipId: e.membershipId, actionError: null));
    try {
      await _gateway.updateMembership(
        companyId: _companyId,
        membershipId: e.membershipId,
        request: UpdateMembershipRequest(role: e.role),
      );
      final refreshed = await _gateway.getById(_companyId);
      emit(MembershipsLoaded(company: refreshed));
    } on Object catch (_) {
      emit(s.copyWith(
        busyMembershipId: null,
        actionError: 'memberships.role_change_failed',
      ));
    }
  }

  Future<void> _onRemove(
    MembershipsRemoveRequested e,
    Emitter<MembershipsState> emit,
  ) async {
    final s = state;
    if (s is! MembershipsLoaded || s.busyMembershipId != null) return;
    if (_wouldRemoveLastAdmin(s.company, e.membershipId)) {
      emit(s.copyWith(actionError: 'memberships.last_admin_protected'));
      return;
    }
    emit(s.copyWith(busyMembershipId: e.membershipId, actionError: null));
    try {
      await _gateway.deleteMembership(
        companyId: _companyId,
        membershipId: e.membershipId,
      );
      final refreshed = await _gateway.getById(_companyId);
      emit(MembershipsLoaded(company: refreshed));
    } on Object catch (_) {
      emit(s.copyWith(
        busyMembershipId: null,
        actionError: 'memberships.remove_failed',
      ));
    }
  }
}
