import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class BranchesEvent {
  const BranchesEvent();
}

class BranchesStarted extends BranchesEvent {
  const BranchesStarted();
}

class BranchesRefreshed extends BranchesEvent {
  const BranchesRefreshed();
}

class BranchesAddRequested extends BranchesEvent {
  const BranchesAddRequested({required this.name, required this.address});
  final String name;
  final String address;
}

class BranchesDeleteRequested extends BranchesEvent {
  const BranchesDeleteRequested(this.branchId);
  final String branchId;
}

@immutable
sealed class BranchesState {
  const BranchesState();
}

class BranchesLoading extends BranchesState {
  const BranchesLoading();
}

class BranchesLoadFailure extends BranchesState {
  const BranchesLoadFailure({required this.reason});
  final String reason;
}

class BranchesLoaded extends BranchesState {
  const BranchesLoaded({
    required this.company,
    this.busyBranchId,
    this.adding = false,
    this.actionError,
  });

  final Company company;
  final String? busyBranchId;
  final bool adding;
  final String? actionError;

  BranchesLoaded copyWith({
    Company? company,
    Object? busyBranchId = _sentinel,
    bool? adding,
    Object? actionError = _sentinel,
  }) {
    return BranchesLoaded(
      company: company ?? this.company,
      busyBranchId: identical(busyBranchId, _sentinel)
          ? this.busyBranchId
          : busyBranchId as String?,
      adding: adding ?? this.adding,
      actionError: identical(actionError, _sentinel)
          ? this.actionError
          : actionError as String?,
    );
  }
}

const _sentinel = Object();

/// Bloc for S-8.9 — branches list + add + delete. Admin-only (BR-4),
/// gated at the route + screen level; the bloc trusts that and lets
/// the server enforce.
class BranchesBloc extends Bloc<BranchesEvent, BranchesState> {
  BranchesBloc({
    required CompaniesGateway gateway,
    required String companyId,
  })  : _gateway = gateway,
        _companyId = companyId,
        super(const BranchesLoading()) {
    on<BranchesStarted>(_load);
    on<BranchesRefreshed>(_load);
    on<BranchesAddRequested>(_onAdd);
    on<BranchesDeleteRequested>(_onDelete);
  }

  final CompaniesGateway _gateway;
  final String _companyId;

  Future<void> _load(
    BranchesEvent e,
    Emitter<BranchesState> emit,
  ) async {
    if (state is! BranchesLoaded) emit(const BranchesLoading());
    try {
      final company = await _gateway.getById(_companyId);
      emit(BranchesLoaded(company: company));
    } on Object catch (_) {
      emit(const BranchesLoadFailure(reason: 'company.load_failed'));
    }
  }

  Future<void> _onAdd(
    BranchesAddRequested e,
    Emitter<BranchesState> emit,
  ) async {
    final s = state;
    if (s is! BranchesLoaded || s.adding) return;
    emit(s.copyWith(adding: true, actionError: null));
    try {
      await _gateway.addBranch(
        companyId: _companyId,
        request: CreateBranchRequest(name: e.name, address: e.address),
      );
      final refreshed = await _gateway.getById(_companyId);
      emit(BranchesLoaded(company: refreshed));
    } on Object catch (_) {
      emit(s.copyWith(adding: false, actionError: 'branches.add_failed'));
    }
  }

  Future<void> _onDelete(
    BranchesDeleteRequested e,
    Emitter<BranchesState> emit,
  ) async {
    final s = state;
    if (s is! BranchesLoaded || s.busyBranchId != null) return;
    emit(s.copyWith(busyBranchId: e.branchId, actionError: null));
    try {
      await _gateway.deleteBranch(
        companyId: _companyId,
        branchId: e.branchId,
      );
      final refreshed = await _gateway.getById(_companyId);
      emit(BranchesLoaded(company: refreshed));
    } on Object catch (_) {
      emit(s.copyWith(
        busyBranchId: null,
        actionError: 'branches.delete_failed',
      ));
    }
  }
}
