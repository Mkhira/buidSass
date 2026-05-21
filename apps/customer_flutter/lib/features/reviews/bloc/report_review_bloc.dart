import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/review_models.dart';
import '../data/reviews_customer_gateway.dart';

@immutable
sealed class ReportReviewEvent {
  const ReportReviewEvent();
}

class ReportReviewStarted extends ReportReviewEvent {
  const ReportReviewStarted();
}

class ReportReviewReasonSelected extends ReportReviewEvent {
  const ReportReviewReasonSelected(this.reasonKey);
  final String reasonKey;
}

class ReportReviewNoteChanged extends ReportReviewEvent {
  const ReportReviewNoteChanged(this.value);
  final String value;
}

class ReportReviewSubmitted extends ReportReviewEvent {
  const ReportReviewSubmitted();
}

@immutable
sealed class ReportReviewState {
  const ReportReviewState();
}

class ReportReviewLoading extends ReportReviewState {
  const ReportReviewLoading();
}

class ReportReviewLoadFailure extends ReportReviewState {
  const ReportReviewLoadFailure({required this.reason});
  final String reason;
}

class ReportReviewReady extends ReportReviewState {
  const ReportReviewReady({
    required this.reasons,
    required this.selectedReason,
    required this.note,
    this.formError,
  });

  final List<ReportReason> reasons;
  final String? selectedReason;
  final String note;
  final String? formError;

  bool get canSubmit => selectedReason != null && selectedReason!.isNotEmpty;

  ReportReviewReady copyWith({
    String? selectedReason,
    String? note,
    Object? formError = _sentinel,
  }) {
    return ReportReviewReady(
      reasons: reasons,
      selectedReason: selectedReason ?? this.selectedReason,
      note: note ?? this.note,
      formError:
          identical(formError, _sentinel) ? this.formError : formError as String?,
    );
  }
}

class ReportReviewSubmitting extends ReportReviewState {
  const ReportReviewSubmitting(this.ready);
  final ReportReviewReady ready;
}

class ReportReviewDone extends ReportReviewState {
  const ReportReviewDone();
}

const _sentinel = Object();

/// Bloc for S-7.8 — report someone else's review. Reasons come from
/// the server per BR-9 (per-market enum).
class ReportReviewBloc extends Bloc<ReportReviewEvent, ReportReviewState> {
  ReportReviewBloc({
    required ReviewsCustomerGateway gateway,
    required String reviewId,
  })  : _gateway = gateway,
        _reviewId = reviewId,
        super(const ReportReviewLoading()) {
    on<ReportReviewStarted>(_load);
    on<ReportReviewReasonSelected>(_onReason);
    on<ReportReviewNoteChanged>(_onNote);
    on<ReportReviewSubmitted>(_onSubmitted);
  }

  final ReviewsCustomerGateway _gateway;
  final String _reviewId;

  Future<void> _load(
    ReportReviewStarted e,
    Emitter<ReportReviewState> emit,
  ) async {
    emit(const ReportReviewLoading());
    try {
      final reasons = await _gateway.getReportReasons();
      emit(ReportReviewReady(reasons: reasons, selectedReason: null, note: ''));
    } on Object catch (err) {
      emit(ReportReviewLoadFailure(reason: err.toString()));
    }
  }

  void _onReason(
    ReportReviewReasonSelected e,
    Emitter<ReportReviewState> emit,
  ) {
    final s = state;
    if (s is! ReportReviewReady) return;
    emit(s.copyWith(selectedReason: e.reasonKey));
  }

  void _onNote(
    ReportReviewNoteChanged e,
    Emitter<ReportReviewState> emit,
  ) {
    final s = state;
    if (s is! ReportReviewReady) return;
    emit(s.copyWith(note: e.value));
  }

  Future<void> _onSubmitted(
    ReportReviewSubmitted e,
    Emitter<ReportReviewState> emit,
  ) async {
    final s = state;
    if (s is! ReportReviewReady || !s.canSubmit) return;
    emit(ReportReviewSubmitting(s));
    try {
      await _gateway.report(
        reviewId: _reviewId,
        request: ReportReviewRequest(
          reasonKey: s.selectedReason!,
          note: s.note.isEmpty ? null : s.note,
        ),
      );
      emit(const ReportReviewDone());
    } on Object catch (err) {
      emit(s.copyWith(formError: err.toString()));
    }
  }
}
