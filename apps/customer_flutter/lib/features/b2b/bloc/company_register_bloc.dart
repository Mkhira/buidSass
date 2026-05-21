import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/companies_gateway.dart';
import '../data/models/company_models.dart';

@immutable
sealed class CompanyRegisterEvent {
  const CompanyRegisterEvent();
}

class CompanyRegisterStarted extends CompanyRegisterEvent {
  const CompanyRegisterStarted({required this.marketCode});
  final String marketCode;
}

class CompanyRegisterFieldChanged extends CompanyRegisterEvent {
  const CompanyRegisterFieldChanged({required this.key, required this.value});
  final String key;
  final String value;
}

class CompanyRegisterSubmitted extends CompanyRegisterEvent {
  const CompanyRegisterSubmitted();
}

@immutable
sealed class CompanyRegisterState {
  const CompanyRegisterState();
}

class CompanyRegisterForm extends CompanyRegisterState {
  const CompanyRegisterForm({
    required this.marketCode,
    required this.name,
    required this.vatNumber,
    required this.address,
    required this.commercialRegistration,
    this.formError,
  });

  final String marketCode;
  final String name;
  final String vatNumber;
  final String address;
  final String commercialRegistration;
  final String? formError;

  bool get canSubmit =>
      name.trim().isNotEmpty &&
      vatNumber.trim().isNotEmpty &&
      address.trim().isNotEmpty;

  CompanyRegisterForm copyWith({
    String? marketCode,
    String? name,
    String? vatNumber,
    String? address,
    String? commercialRegistration,
    Object? formError = _sentinel,
  }) {
    return CompanyRegisterForm(
      marketCode: marketCode ?? this.marketCode,
      name: name ?? this.name,
      vatNumber: vatNumber ?? this.vatNumber,
      address: address ?? this.address,
      commercialRegistration:
          commercialRegistration ?? this.commercialRegistration,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class CompanyRegisterSubmitting extends CompanyRegisterState {
  const CompanyRegisterSubmitting(this.form);
  final CompanyRegisterForm form;
}

class CompanyRegisterDone extends CompanyRegisterState {
  const CompanyRegisterDone(this.result);
  final CreateCompanyResult result;
}

const _sentinel = Object();

class CompanyRegisterBloc
    extends Bloc<CompanyRegisterEvent, CompanyRegisterState> {
  CompanyRegisterBloc({
    required CompaniesGateway gateway,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const CompanyRegisterForm(
          marketCode: 'SA',
          name: '',
          vatNumber: '',
          address: '',
          commercialRegistration: '',
        )) {
    on<CompanyRegisterStarted>(_onStarted);
    on<CompanyRegisterFieldChanged>(_onFieldChanged);
    on<CompanyRegisterSubmitted>(_onSubmitted);
  }

  final CompaniesGateway _gateway;
  final String _idempotencyKey;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  void _onStarted(
    CompanyRegisterStarted e,
    Emitter<CompanyRegisterState> emit,
  ) {
    final s = state;
    if (s is! CompanyRegisterForm) return;
    emit(s.copyWith(marketCode: e.marketCode));
  }

  void _onFieldChanged(
    CompanyRegisterFieldChanged e,
    Emitter<CompanyRegisterState> emit,
  ) {
    final s = state;
    if (s is! CompanyRegisterForm) return;
    switch (e.key) {
      case 'name':
        emit(s.copyWith(name: e.value));
        break;
      case 'vatNumber':
        emit(s.copyWith(vatNumber: e.value));
        break;
      case 'address':
        emit(s.copyWith(address: e.value));
        break;
      case 'commercialRegistration':
        emit(s.copyWith(commercialRegistration: e.value));
        break;
    }
  }

  Future<void> _onSubmitted(
    CompanyRegisterSubmitted e,
    Emitter<CompanyRegisterState> emit,
  ) async {
    final s = state;
    if (s is! CompanyRegisterForm || !s.canSubmit) return;
    emit(CompanyRegisterSubmitting(s));
    try {
      final result = await _gateway.create(
        request: CreateCompanyRequest(
          name: s.name.trim(),
          vatNumber: s.vatNumber.trim(),
          address: s.address.trim(),
          marketCode: s.marketCode,
          commercialRegistration: s.commercialRegistration.isEmpty
              ? null
              : s.commercialRegistration.trim(),
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(CompanyRegisterDone(result));
    } on Object catch (_) {
      emit(s.copyWith(formError: 'company.create_failed'));
    }
  }
}
