import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../data/invoices_gateway.dart';
import '../data/models/invoice_models.dart';

@immutable
sealed class InvoicePreviewState {
  const InvoicePreviewState();
}

class InvoicePreviewLoading extends InvoicePreviewState {
  const InvoicePreviewLoading();
}

class InvoicePreviewLoaded extends InvoicePreviewState {
  const InvoicePreviewLoaded({required this.preview});
  final InvoicePreview preview;
}

/// 404 from the JSON preview endpoint — bank transfer not yet
/// captured / invoice not yet generated. Per BR-6 + S-6.4, the screen
/// renders a friendly "Available after payment is captured" empty
/// state instead of an error.
class InvoicePreviewUnavailable extends InvoicePreviewState {
  const InvoicePreviewUnavailable();
}

class InvoicePreviewFailure extends InvoicePreviewState {
  const InvoicePreviewFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

@immutable
sealed class InvoicePreviewEvent {
  const InvoicePreviewEvent();
}

class InvoicePreviewStarted extends InvoicePreviewEvent {
  const InvoicePreviewStarted();
}

class InvoicePreviewRefreshed extends InvoicePreviewEvent {
  const InvoicePreviewRefreshed();
}

/// Bloc for S-6.4 invoice preview. 404 routes to a friendly
/// `InvoicePreviewUnavailable` state (BR-6); other failures go through
/// the standard error path.
class InvoicePreviewBloc
    extends Bloc<InvoicePreviewEvent, InvoicePreviewState> {
  InvoicePreviewBloc({required InvoicesGateway gateway, required this.orderId})
      : _gateway = gateway,
        super(const InvoicePreviewLoading()) {
    on<InvoicePreviewStarted>(_load);
    on<InvoicePreviewRefreshed>(_load);
  }

  final InvoicesGateway _gateway;
  final String orderId;

  Future<void> _load(
    InvoicePreviewEvent event,
    Emitter<InvoicePreviewState> emit,
  ) async {
    emit(const InvoicePreviewLoading());
    try {
      final preview = await _gateway.getPreview(orderId);
      emit(InvoicePreviewLoaded(preview: preview));
    } on NotFoundFailure {
      // BR-6: invoice not yet ready (bank transfer pending,
      // captured-but-not-rendered, etc.). The empty state copy is
      // explicit: "Available after payment captures". No retry CTA —
      // the screen offers a pull-to-refresh.
      emit(const InvoicePreviewUnavailable());
    } on Failure catch (f) {
      emit(InvoicePreviewFailure(
          reason: f.message, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(InvoicePreviewFailure(reason: e.toString()));
    }
  }
}
