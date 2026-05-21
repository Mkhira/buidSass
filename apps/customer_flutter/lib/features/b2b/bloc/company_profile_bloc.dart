import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class CompanyProfileEvent {
  const CompanyProfileEvent();
}

class CompanyProfileStarted extends CompanyProfileEvent {
  const CompanyProfileStarted();
}

class CompanyProfileRefreshed extends CompanyProfileEvent {
  const CompanyProfileRefreshed();
}

class CompanyProfileEditToggled extends CompanyProfileEvent {
  const CompanyProfileEditToggled();
}

class CompanyProfileFieldChanged extends CompanyProfileEvent {
  const CompanyProfileFieldChanged({required this.key, required this.value});
  final String key;
  final String value;
}

class CompanyProfileSaved extends CompanyProfileEvent {
  const CompanyProfileSaved();
}

@immutable
sealed class CompanyProfileState {
  const CompanyProfileState();
}

class CompanyProfileLoading extends CompanyProfileState {
  const CompanyProfileLoading();
}

class CompanyProfileLoadFailure extends CompanyProfileState {
  const CompanyProfileLoadFailure({required this.reason});
  final String reason;
}

class CompanyProfileLoaded extends CompanyProfileState {
  const CompanyProfileLoaded({
    required this.company,
    required this.editing,
    required this.draft,
    this.saveError,
  });

  final Company company;
  final bool editing;

  /// Pending edits. Reset to the company's values when leaving edit
  /// mode without saving.
  final UpdateCompanyRequest draft;
  final String? saveError;

  CompanyProfileLoaded copyWith({
    Company? company,
    bool? editing,
    UpdateCompanyRequest? draft,
    Object? saveError = _sentinel,
  }) {
    return CompanyProfileLoaded(
      company: company ?? this.company,
      editing: editing ?? this.editing,
      draft: draft ?? this.draft,
      saveError: identical(saveError, _sentinel)
          ? this.saveError
          : saveError as String?,
    );
  }
}

class CompanyProfileSaving extends CompanyProfileState {
  const CompanyProfileSaving(this.loaded);
  final CompanyProfileLoaded loaded;
}

const _sentinel = Object();

/// Plan §"Role gating" — this bloc is the single source of truth for
/// `myRole` throughout Phase 8. Branches / Memberships / Approver-only
/// route guards all consult this bloc's loaded state.
class CompanyProfileBloc
    extends Bloc<CompanyProfileEvent, CompanyProfileState> {
  CompanyProfileBloc({
    required CompaniesGateway gateway,
    required String companyId,
  })  : _gateway = gateway,
        _companyId = companyId,
        super(const CompanyProfileLoading()) {
    on<CompanyProfileStarted>(_load);
    on<CompanyProfileRefreshed>(_load);
    on<CompanyProfileEditToggled>(_onToggle);
    on<CompanyProfileFieldChanged>(_onFieldChanged);
    on<CompanyProfileSaved>(_onSaved);
  }

  final CompaniesGateway _gateway;
  final String _companyId;

  Future<void> _load(
    CompanyProfileEvent e,
    Emitter<CompanyProfileState> emit,
  ) async {
    if (state is! CompanyProfileLoaded) {
      emit(const CompanyProfileLoading());
    }
    try {
      final company = await _gateway.getById(_companyId);
      emit(CompanyProfileLoaded(
        company: company,
        editing: false,
        draft: const UpdateCompanyRequest(),
      ));
    } on Object catch (_) {
      emit(const CompanyProfileLoadFailure(reason: 'company.load_failed'));
    }
  }

  void _onToggle(
    CompanyProfileEditToggled e,
    Emitter<CompanyProfileState> emit,
  ) {
    final s = state;
    if (s is! CompanyProfileLoaded) return;
    if (!s.company.isAdmin && !s.editing) return;
    emit(s.copyWith(
      editing: !s.editing,
      draft: const UpdateCompanyRequest(),
      saveError: null,
    ));
  }

  void _onFieldChanged(
    CompanyProfileFieldChanged e,
    Emitter<CompanyProfileState> emit,
  ) {
    final s = state;
    if (s is! CompanyProfileLoaded || !s.editing) return;
    final d = s.draft;
    UpdateCompanyRequest next;
    switch (e.key) {
      case 'name':
        next = UpdateCompanyRequest(
          name: e.value,
          vatNumber: d.vatNumber,
          address: d.address,
          commercialRegistration: d.commercialRegistration,
        );
        break;
      case 'vatNumber':
        next = UpdateCompanyRequest(
          name: d.name,
          vatNumber: e.value,
          address: d.address,
          commercialRegistration: d.commercialRegistration,
        );
        break;
      case 'address':
        next = UpdateCompanyRequest(
          name: d.name,
          vatNumber: d.vatNumber,
          address: e.value,
          commercialRegistration: d.commercialRegistration,
        );
        break;
      case 'commercialRegistration':
        next = UpdateCompanyRequest(
          name: d.name,
          vatNumber: d.vatNumber,
          address: d.address,
          commercialRegistration: e.value,
        );
        break;
      default:
        return;
    }
    emit(s.copyWith(draft: next));
  }

  Future<void> _onSaved(
    CompanyProfileSaved e,
    Emitter<CompanyProfileState> emit,
  ) async {
    final s = state;
    if (s is! CompanyProfileLoaded || !s.editing) return;
    emit(CompanyProfileSaving(s));
    try {
      final updated = await _gateway.update(
        id: _companyId,
        request: s.draft,
      );
      emit(CompanyProfileLoaded(
        company: updated,
        editing: false,
        draft: const UpdateCompanyRequest(),
      ));
    } on Object catch (err) {
      final msg = err.toString();
      // BR-4 + plan §Risk 2: a 403 means the admin was demoted; refresh
      // to surface read-only mode.
      if (msg.contains('403') || msg.contains('forbidden')) {
        try {
          final refreshed = await _gateway.getById(_companyId);
          emit(CompanyProfileLoaded(
            company: refreshed,
            editing: false,
            draft: const UpdateCompanyRequest(),
            saveError: 'company.role_changed',
          ));
          return;
        } on Object catch (_) {
          // fall through
        }
      }
      emit(s.copyWith(saveError: 'company.save_failed'));
    }
  }
}
