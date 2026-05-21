import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../../cart/data/cart_store.dart';
import '../data/models/quote_models.dart';
import '../data/quotes_gateway.dart';

@immutable
sealed class QuoteFromCartEvent {
  const QuoteFromCartEvent();
}

class QuoteFromCartStarted extends QuoteFromCartEvent {
  const QuoteFromCartStarted();
}

class QuoteFromCartTermsChanged extends QuoteFromCartEvent {
  const QuoteFromCartTermsChanged(this.value);
  final String value;
}

class QuoteFromCartEtaChanged extends QuoteFromCartEvent {
  const QuoteFromCartEtaChanged(this.value);
  final DateTime? value;
}

class QuoteFromCartNoteChanged extends QuoteFromCartEvent {
  const QuoteFromCartNoteChanged(this.value);
  final String value;
}

class QuoteFromCartSubmitted extends QuoteFromCartEvent {
  const QuoteFromCartSubmitted();
}

@immutable
sealed class QuoteFromCartState {
  const QuoteFromCartState();
}

class QuoteFromCartEmpty extends QuoteFromCartState {
  const QuoteFromCartEmpty();
}

class QuoteFromCartForm extends QuoteFromCartState {
  const QuoteFromCartForm({
    required this.cartLines,
    required this.terms,
    required this.note,
    this.eta,
    this.formError,
  });

  final List<({String productId, int qty})> cartLines;
  final String terms;
  final String note;
  final DateTime? eta;
  final String? formError;

  bool get canSubmit => cartLines.isNotEmpty && terms.trim().isNotEmpty;

  QuoteFromCartForm copyWith({
    String? terms,
    String? note,
    Object? eta = _sentinel,
    Object? formError = _sentinel,
  }) {
    return QuoteFromCartForm(
      cartLines: cartLines,
      terms: terms ?? this.terms,
      note: note ?? this.note,
      eta: identical(eta, _sentinel) ? this.eta : eta as DateTime?,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class QuoteFromCartSubmitting extends QuoteFromCartState {
  const QuoteFromCartSubmitting(this.form);
  final QuoteFromCartForm form;
}

class QuoteFromCartDone extends QuoteFromCartState {
  const QuoteFromCartDone(this.result);
  final CreateQuoteResult result;
}

const _sentinel = Object();

/// Bloc for S-8.3 — request quote from cart. Cart snapshot is taken
/// once on entry (BR: "Submit reuses cart contents at the moment of
/// entry (no mutation)"). Idempotency-Key is locked in at construction.
class QuoteFromCartBloc extends Bloc<QuoteFromCartEvent, QuoteFromCartState> {
  QuoteFromCartBloc({
    required QuotesGateway gateway,
    required CartStore cartStore,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _cartStore = cartStore,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const QuoteFromCartEmpty()) {
    on<QuoteFromCartStarted>(_onStarted);
    on<QuoteFromCartTermsChanged>(_onTerms);
    on<QuoteFromCartEtaChanged>(_onEta);
    on<QuoteFromCartNoteChanged>(_onNote);
    on<QuoteFromCartSubmitted>(_onSubmitted);
  }

  final QuotesGateway _gateway;
  final CartStore _cartStore;
  final String _idempotencyKey;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  void _onStarted(QuoteFromCartStarted e, Emitter<QuoteFromCartState> emit) {
    // Snapshot the cart at entry — subsequent cart mutations don't
    // affect this quote intent.
    final snapshot = _cartStore.snapshot;
    if (snapshot.lines.isEmpty) {
      emit(const QuoteFromCartEmpty());
      return;
    }
    emit(QuoteFromCartForm(
      cartLines: [
        for (final l in snapshot.lines) (productId: l.productId, qty: l.qty),
      ],
      terms: '',
      note: '',
    ));
  }

  void _onTerms(
    QuoteFromCartTermsChanged e,
    Emitter<QuoteFromCartState> emit,
  ) {
    final s = state;
    if (s is! QuoteFromCartForm) return;
    emit(s.copyWith(terms: e.value));
  }

  void _onEta(QuoteFromCartEtaChanged e, Emitter<QuoteFromCartState> emit) {
    final s = state;
    if (s is! QuoteFromCartForm) return;
    emit(s.copyWith(eta: e.value));
  }

  void _onNote(QuoteFromCartNoteChanged e, Emitter<QuoteFromCartState> emit) {
    final s = state;
    if (s is! QuoteFromCartForm) return;
    emit(s.copyWith(note: e.value));
  }

  Future<void> _onSubmitted(
    QuoteFromCartSubmitted e,
    Emitter<QuoteFromCartState> emit,
  ) async {
    final s = state;
    if (s is! QuoteFromCartForm || !s.canSubmit) return;
    emit(QuoteFromCartSubmitting(s));
    try {
      final result = await _gateway.createFromCart(
        request: CreateQuoteFromCartRequest(
          cartLines: s.cartLines,
          terms: s.terms.trim(),
          expectedDeliveryDate: s.eta,
          note: s.note.isEmpty ? null : s.note,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(QuoteFromCartDone(result));
    } on Object catch (_) {
      emit(s.copyWith(formError: 'quote.create_failed'));
    }
  }
}
