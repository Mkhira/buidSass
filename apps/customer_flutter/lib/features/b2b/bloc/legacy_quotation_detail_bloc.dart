import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/legacy_quotations_gateway.dart';
import '../data/models/legacy_quotation_models.dart';

@immutable
sealed class LegacyQuotationDetailEvent {
  const LegacyQuotationDetailEvent();
}

class LegacyQuotationDetailStarted extends LegacyQuotationDetailEvent {
  const LegacyQuotationDetailStarted();
}

class LegacyQuotationAccepted extends LegacyQuotationDetailEvent {
  const LegacyQuotationAccepted({this.note});
  final String? note;
}

class LegacyQuotationRejected extends LegacyQuotationDetailEvent {
  const LegacyQuotationRejected({this.note});
  final String? note;
}

@immutable
sealed class LegacyQuotationDetailState {
  const LegacyQuotationDetailState();
}

class LegacyQuotationDetailLoading extends LegacyQuotationDetailState {
  const LegacyQuotationDetailLoading();
}

class LegacyQuotationDetailLoadFailure extends LegacyQuotationDetailState {
  const LegacyQuotationDetailLoadFailure({required this.reason});
  final String reason;
}

class LegacyQuotationDetailLoaded extends LegacyQuotationDetailState {
  const LegacyQuotationDetailLoaded({
    required this.quotation,
    this.busy = false,
    this.actionError,
  });

  final LegacyQuotationDetail quotation;
  final bool busy;
  final String? actionError;

  LegacyQuotationDetailLoaded copyWith({
    LegacyQuotationDetail? quotation,
    bool? busy,
    Object? actionError = _sentinel,
  }) {
    return LegacyQuotationDetailLoaded(
      quotation: quotation ?? this.quotation,
      busy: busy ?? this.busy,
      actionError: identical(actionError, _sentinel)
          ? this.actionError
          : actionError as String?,
    );
  }
}

const _sentinel = Object();

class LegacyQuotationDetailBloc
    extends Bloc<LegacyQuotationDetailEvent, LegacyQuotationDetailState> {
  LegacyQuotationDetailBloc({
    required LegacyQuotationsGateway gateway,
    required String quotationId,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _quotationId = quotationId,
        _newKey = idempotencyKeyFactory ?? const Uuid().v4,
        super(const LegacyQuotationDetailLoading()) {
    on<LegacyQuotationDetailStarted>(_load);
    on<LegacyQuotationAccepted>(_onAccepted);
    on<LegacyQuotationRejected>(_onRejected);
  }

  final LegacyQuotationsGateway _gateway;
  final String _quotationId;
  final String Function() _newKey;

  Future<void> _load(
    LegacyQuotationDetailEvent e,
    Emitter<LegacyQuotationDetailState> emit,
  ) async {
    emit(const LegacyQuotationDetailLoading());
    try {
      final detail = await _gateway.getById(_quotationId);
      emit(LegacyQuotationDetailLoaded(quotation: detail));
    } on Object catch (_) {
      emit(const LegacyQuotationDetailLoadFailure(
        reason: 'legacy_quotation.load_failed',
      ));
    }
  }

  Future<void> _onAccepted(
    LegacyQuotationAccepted e,
    Emitter<LegacyQuotationDetailState> emit,
  ) =>
      _act(
        emit,
        (key) => _gateway.accept(
          id: _quotationId,
          request: LegacyQuotationActionRequest(note: e.note),
          idempotencyKey: key,
        ),
      );

  Future<void> _onRejected(
    LegacyQuotationRejected e,
    Emitter<LegacyQuotationDetailState> emit,
  ) =>
      _act(
        emit,
        (key) => _gateway.reject(
          id: _quotationId,
          request: LegacyQuotationActionRequest(note: e.note),
          idempotencyKey: key,
        ),
      );

  Future<void> _act(
    Emitter<LegacyQuotationDetailState> emit,
    Future<LegacyQuotationDetail> Function(String key) call,
  ) async {
    final s = state;
    if (s is! LegacyQuotationDetailLoaded || s.busy) return;
    emit(s.copyWith(busy: true, actionError: null));
    try {
      final updated = await call(_newKey());
      emit(LegacyQuotationDetailLoaded(quotation: updated));
    } on Object catch (_) {
      emit(s.copyWith(
          busy: false, actionError: 'legacy_quotation.action_failed'));
    }
  }
}
