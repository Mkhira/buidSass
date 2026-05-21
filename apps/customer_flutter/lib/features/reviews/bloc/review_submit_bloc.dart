import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/review_models.dart';
import '../data/reviews_customer_gateway.dart';

// ============================================================
// Events
// ============================================================

@immutable
sealed class ReviewSubmitEvent {
  const ReviewSubmitEvent();
}

class ReviewSubmitStarted extends ReviewSubmitEvent {
  const ReviewSubmitStarted({
    required this.productId,
    required this.orderId,
    required this.locale,
  });
  final String productId;
  final String orderId;
  final String locale;
}

class ReviewSubmitRatingChanged extends ReviewSubmitEvent {
  const ReviewSubmitRatingChanged(this.value);
  final int value;
}

class ReviewSubmitCommentChanged extends ReviewSubmitEvent {
  const ReviewSubmitCommentChanged(this.value);
  final String value;
}

class ReviewSubmitLocaleChanged extends ReviewSubmitEvent {
  const ReviewSubmitLocaleChanged(this.value);
  final String value;
}

class ReviewSubmitSubmitted extends ReviewSubmitEvent {
  const ReviewSubmitSubmitted();
}

// ============================================================
// State
// ============================================================

@immutable
sealed class ReviewSubmitState {
  const ReviewSubmitState();
}

class ReviewSubmitForm extends ReviewSubmitState {
  const ReviewSubmitForm({
    required this.productId,
    required this.orderId,
    required this.rating,
    required this.comment,
    required this.locale,
    this.formError,
  });

  final String productId;
  final String orderId;
  final int rating;
  final String comment;
  final String locale;
  final String? formError;

  bool get canSubmit => rating >= 1 && rating <= 5 && comment.trim().isNotEmpty;

  ReviewSubmitForm copyWith({
    int? rating,
    String? comment,
    String? locale,
    Object? formError = _sentinel,
  }) {
    return ReviewSubmitForm(
      productId: productId,
      orderId: orderId,
      rating: rating ?? this.rating,
      comment: comment ?? this.comment,
      locale: locale ?? this.locale,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class ReviewSubmitSubmitting extends ReviewSubmitState {
  const ReviewSubmitSubmitting(this.form);
  final ReviewSubmitForm form;
}

class ReviewSubmitDone extends ReviewSubmitState {
  const ReviewSubmitDone(this.result);
  final CreateReviewResult result;
}

/// 403 — server says "verified buyer only" (BR-6). The screen shows
/// a friendly "buy this product to review it" empty state.
class ReviewSubmitNotEligible extends ReviewSubmitState {
  const ReviewSubmitNotEligible();
}

const _sentinel = Object();

// ============================================================
// Bloc
// ============================================================

/// Bloc for S-7.5 review submit. Single Idempotency-Key locked in at
/// construction (BR-7). 403 surfaces as a `NotEligible` terminal state
/// per BR-6.
class ReviewSubmitBloc extends Bloc<ReviewSubmitEvent, ReviewSubmitState> {
  ReviewSubmitBloc({
    required ReviewsCustomerGateway gateway,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const ReviewSubmitForm(
          productId: '',
          orderId: '',
          rating: 0,
          comment: '',
          locale: 'en',
        )) {
    on<ReviewSubmitStarted>(_onStarted);
    on<ReviewSubmitRatingChanged>(_onRatingChanged);
    on<ReviewSubmitCommentChanged>(_onCommentChanged);
    on<ReviewSubmitLocaleChanged>(_onLocaleChanged);
    on<ReviewSubmitSubmitted>(_onSubmitted);
  }

  final ReviewsCustomerGateway _gateway;
  final String _idempotencyKey;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  void _onStarted(
    ReviewSubmitStarted event,
    Emitter<ReviewSubmitState> emit,
  ) {
    emit(ReviewSubmitForm(
      productId: event.productId,
      orderId: event.orderId,
      rating: 0,
      comment: '',
      locale: event.locale,
    ));
  }

  void _onRatingChanged(
    ReviewSubmitRatingChanged event,
    Emitter<ReviewSubmitState> emit,
  ) {
    final s = state;
    if (s is! ReviewSubmitForm) return;
    final clamped = event.value < 0 ? 0 : (event.value > 5 ? 5 : event.value);
    emit(s.copyWith(rating: clamped));
  }

  void _onCommentChanged(
    ReviewSubmitCommentChanged event,
    Emitter<ReviewSubmitState> emit,
  ) {
    final s = state;
    if (s is! ReviewSubmitForm) return;
    // Comment cap from spec (≤ 2000 chars) — enforced here, server is
    // the final word.
    final capped = event.value.length > 2000
        ? event.value.substring(0, 2000)
        : event.value;
    emit(s.copyWith(comment: capped));
  }

  void _onLocaleChanged(
    ReviewSubmitLocaleChanged event,
    Emitter<ReviewSubmitState> emit,
  ) {
    final s = state;
    if (s is! ReviewSubmitForm) return;
    emit(s.copyWith(locale: event.value));
  }

  Future<void> _onSubmitted(
    ReviewSubmitSubmitted event,
    Emitter<ReviewSubmitState> emit,
  ) async {
    final s = state;
    if (s is! ReviewSubmitForm) return;
    if (!s.canSubmit) {
      emit(s.copyWith(formError: 'reviewSubmitErrorIncomplete'));
      return;
    }
    emit(ReviewSubmitSubmitting(s));
    try {
      final result = await _gateway.submit(
        request: CreateReviewRequest(
          productId: s.productId,
          orderId: s.orderId,
          rating: s.rating,
          comment: s.comment.trim(),
          locale: s.locale,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(ReviewSubmitDone(result));
    } on Object catch (e) {
      // BR-6: 403 from server → NotEligible empty state. The
      // ErrorMapper surfaces 403 as `ForbiddenFailure` with code
      // starting `http.403` or `auth.forbidden` — we sniff the
      // stringification because the bloc doesn't depend on the
      // Failure types directly (mirrors the wider phase pattern of
      // bubbling `Object` errors).
      final msg = e.toString();
      if (msg.contains('403') || msg.contains('forbidden')) {
        emit(const ReviewSubmitNotEligible());
        return;
      }
      emit(s.copyWith(formError: msg));
    }
  }
}
