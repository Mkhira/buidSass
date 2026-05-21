import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/review_models.dart';
import '../data/reviews_customer_gateway.dart';

@immutable
sealed class MyReviewDetailEvent {
  const MyReviewDetailEvent();
}

class MyReviewDetailStarted extends MyReviewDetailEvent {
  const MyReviewDetailStarted();
}

class MyReviewDetailEditToggled extends MyReviewDetailEvent {
  const MyReviewDetailEditToggled();
}

class MyReviewDetailRatingChanged extends MyReviewDetailEvent {
  const MyReviewDetailRatingChanged(this.value);
  final int value;
}

class MyReviewDetailCommentChanged extends MyReviewDetailEvent {
  const MyReviewDetailCommentChanged(this.value);
  final String value;
}

class MyReviewDetailSaved extends MyReviewDetailEvent {
  const MyReviewDetailSaved();
}

@immutable
sealed class MyReviewDetailState {
  const MyReviewDetailState();
}

class MyReviewDetailLoading extends MyReviewDetailState {
  const MyReviewDetailLoading();
}

class MyReviewDetailLoadFailure extends MyReviewDetailState {
  const MyReviewDetailLoadFailure({required this.reason});
  final String reason;
}

class MyReviewDetailLoaded extends MyReviewDetailState {
  const MyReviewDetailLoaded({
    required this.detail,
    required this.editing,
    required this.rating,
    required this.comment,
    this.saveError,
  });

  final MyReviewDetail detail;
  final bool editing;

  /// Pending edits (or original values when not editing).
  final int rating;
  final String comment;
  final String? saveError;

  bool get isEditableNow => detail.isEditableAt(DateTime.now());
  bool get canSave =>
      editing && rating >= 1 && rating <= 5 && comment.trim().isNotEmpty;

  MyReviewDetailLoaded copyWith({
    MyReviewDetail? detail,
    bool? editing,
    int? rating,
    String? comment,
    Object? saveError = _sentinel,
  }) {
    return MyReviewDetailLoaded(
      detail: detail ?? this.detail,
      editing: editing ?? this.editing,
      rating: rating ?? this.rating,
      comment: comment ?? this.comment,
      saveError:
          identical(saveError, _sentinel) ? this.saveError : saveError as String?,
    );
  }
}

class MyReviewDetailSaving extends MyReviewDetailState {
  const MyReviewDetailSaving(this.loaded);
  final MyReviewDetailLoaded loaded;
}

const _sentinel = Object();

/// Bloc for S-7.7 — my review detail + edit. Edit is gated by
/// `editableUntil` (BR-10); the screen checks `isEditableNow` to enable
/// the Edit CTA. Server still owns the final word on save (it returns
/// 409 if the window slipped between the user opening the screen and
/// hitting save).
class MyReviewDetailBloc
    extends Bloc<MyReviewDetailEvent, MyReviewDetailState> {
  MyReviewDetailBloc({
    required ReviewsCustomerGateway gateway,
    required String reviewId,
  })  : _gateway = gateway,
        _reviewId = reviewId,
        super(const MyReviewDetailLoading()) {
    on<MyReviewDetailStarted>(_load);
    on<MyReviewDetailEditToggled>(_onToggle);
    on<MyReviewDetailRatingChanged>(_onRating);
    on<MyReviewDetailCommentChanged>(_onComment);
    on<MyReviewDetailSaved>(_onSaved);
  }

  final ReviewsCustomerGateway _gateway;
  final String _reviewId;

  Future<void> _load(
    MyReviewDetailEvent e,
    Emitter<MyReviewDetailState> emit,
  ) async {
    emit(const MyReviewDetailLoading());
    try {
      final detail = await _gateway.getMine(_reviewId);
      emit(MyReviewDetailLoaded(
        detail: detail,
        editing: false,
        rating: detail.rating,
        comment: detail.comment,
      ));
    } on Object catch (err) {
      emit(MyReviewDetailLoadFailure(reason: err.toString()));
    }
  }

  void _onToggle(
    MyReviewDetailEditToggled e,
    Emitter<MyReviewDetailState> emit,
  ) {
    final s = state;
    if (s is! MyReviewDetailLoaded) return;
    if (!s.isEditableNow && !s.editing) return; // can't enter edit mode
    emit(s.copyWith(
      editing: !s.editing,
      // Reset pending edits when leaving edit mode without saving.
      rating: s.editing ? s.detail.rating : s.rating,
      comment: s.editing ? s.detail.comment : s.comment,
      saveError: null,
    ));
  }

  void _onRating(
    MyReviewDetailRatingChanged e,
    Emitter<MyReviewDetailState> emit,
  ) {
    final s = state;
    if (s is! MyReviewDetailLoaded || !s.editing) return;
    final clamped = e.value < 0 ? 0 : (e.value > 5 ? 5 : e.value);
    emit(s.copyWith(rating: clamped));
  }

  void _onComment(
    MyReviewDetailCommentChanged e,
    Emitter<MyReviewDetailState> emit,
  ) {
    final s = state;
    if (s is! MyReviewDetailLoaded || !s.editing) return;
    final capped =
        e.value.length > 2000 ? e.value.substring(0, 2000) : e.value;
    emit(s.copyWith(comment: capped));
  }

  Future<void> _onSaved(
    MyReviewDetailSaved e,
    Emitter<MyReviewDetailState> emit,
  ) async {
    final s = state;
    if (s is! MyReviewDetailLoaded || !s.canSave) return;
    emit(MyReviewDetailSaving(s));
    try {
      final updated = await _gateway.edit(
        reviewId: _reviewId,
        request: EditReviewRequest(rating: s.rating, comment: s.comment.trim()),
      );
      emit(MyReviewDetailLoaded(
        detail: updated,
        editing: false,
        rating: updated.rating,
        comment: updated.comment,
      ));
    } on Object catch (err) {
      emit(s.copyWith(saveError: err.toString()));
    }
  }
}
