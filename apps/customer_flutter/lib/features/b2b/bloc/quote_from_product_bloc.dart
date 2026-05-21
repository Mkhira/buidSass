import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/quote_models.dart';
import '../data/quotes_gateway.dart';

@immutable
sealed class QuoteFromProductEvent {
  const QuoteFromProductEvent();
}

class QuoteFromProductStarted extends QuoteFromProductEvent {
  const QuoteFromProductStarted({required this.productId});
  final String productId;
}

class QuoteFromProductQtyChanged extends QuoteFromProductEvent {
  const QuoteFromProductQtyChanged(this.value);
  final int value;
}

class QuoteFromProductTermsChanged extends QuoteFromProductEvent {
  const QuoteFromProductTermsChanged(this.value);
  final String value;
}

class QuoteFromProductEtaChanged extends QuoteFromProductEvent {
  const QuoteFromProductEtaChanged(this.value);
  final DateTime? value;
}

class QuoteFromProductNoteChanged extends QuoteFromProductEvent {
  const QuoteFromProductNoteChanged(this.value);
  final String value;
}

class QuoteFromProductSubmitted extends QuoteFromProductEvent {
  const QuoteFromProductSubmitted();
}

@immutable
sealed class QuoteFromProductState {
  const QuoteFromProductState();
}

class QuoteFromProductForm extends QuoteFromProductState {
  const QuoteFromProductForm({
    required this.productId,
    required this.qty,
    required this.terms,
    required this.note,
    this.eta,
    this.formError,
  });

  final String productId;
  final int qty;
  final String terms;
  final String note;
  final DateTime? eta;
  final String? formError;

  bool get canSubmit =>
      productId.isNotEmpty && qty > 0 && terms.trim().isNotEmpty;

  QuoteFromProductForm copyWith({
    int? qty,
    String? terms,
    String? note,
    Object? eta = _sentinel,
    Object? formError = _sentinel,
  }) {
    return QuoteFromProductForm(
      productId: productId,
      qty: qty ?? this.qty,
      terms: terms ?? this.terms,
      note: note ?? this.note,
      eta: identical(eta, _sentinel) ? this.eta : eta as DateTime?,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class QuoteFromProductSubmitting extends QuoteFromProductState {
  const QuoteFromProductSubmitting(this.form);
  final QuoteFromProductForm form;
}

class QuoteFromProductDone extends QuoteFromProductState {
  const QuoteFromProductDone(this.result);
  final CreateQuoteResult result;
}

const _sentinel = Object();

/// Bloc for S-8.4 — request quote from product page. Idempotency-Key
/// locked in at construction; re-entering the screen constructs a
/// fresh bloc + key.
class QuoteFromProductBloc
    extends Bloc<QuoteFromProductEvent, QuoteFromProductState> {
  QuoteFromProductBloc({
    required QuotesGateway gateway,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const QuoteFromProductForm(
          productId: '',
          qty: 1,
          terms: '',
          note: '',
        )) {
    on<QuoteFromProductStarted>(_onStarted);
    on<QuoteFromProductQtyChanged>(_onQty);
    on<QuoteFromProductTermsChanged>(_onTerms);
    on<QuoteFromProductEtaChanged>(_onEta);
    on<QuoteFromProductNoteChanged>(_onNote);
    on<QuoteFromProductSubmitted>(_onSubmitted);
  }

  final QuotesGateway _gateway;
  final String _idempotencyKey;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  void _onStarted(
    QuoteFromProductStarted e,
    Emitter<QuoteFromProductState> emit,
  ) {
    emit(QuoteFromProductForm(
      productId: e.productId,
      qty: 1,
      terms: '',
      note: '',
    ));
  }

  void _onQty(
    QuoteFromProductQtyChanged e,
    Emitter<QuoteFromProductState> emit,
  ) {
    final s = state;
    if (s is! QuoteFromProductForm) return;
    final clamped = e.value < 1 ? 1 : e.value;
    emit(s.copyWith(qty: clamped));
  }

  void _onTerms(
    QuoteFromProductTermsChanged e,
    Emitter<QuoteFromProductState> emit,
  ) {
    final s = state;
    if (s is! QuoteFromProductForm) return;
    emit(s.copyWith(terms: e.value));
  }

  void _onEta(
    QuoteFromProductEtaChanged e,
    Emitter<QuoteFromProductState> emit,
  ) {
    final s = state;
    if (s is! QuoteFromProductForm) return;
    emit(s.copyWith(eta: e.value));
  }

  void _onNote(
    QuoteFromProductNoteChanged e,
    Emitter<QuoteFromProductState> emit,
  ) {
    final s = state;
    if (s is! QuoteFromProductForm) return;
    emit(s.copyWith(note: e.value));
  }

  Future<void> _onSubmitted(
    QuoteFromProductSubmitted e,
    Emitter<QuoteFromProductState> emit,
  ) async {
    final s = state;
    if (s is! QuoteFromProductForm || !s.canSubmit) return;
    emit(QuoteFromProductSubmitting(s));
    try {
      final result = await _gateway.createFromProduct(
        request: CreateQuoteFromProductRequest(
          productId: s.productId,
          qty: s.qty,
          terms: s.terms.trim(),
          expectedDeliveryDate: s.eta,
          note: s.note.isEmpty ? null : s.note,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(QuoteFromProductDone(result));
    } on Object catch (_) {
      emit(s.copyWith(formError: 'quote.create_failed'));
    }
  }
}
