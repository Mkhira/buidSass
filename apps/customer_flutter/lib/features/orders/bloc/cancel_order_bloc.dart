import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../data/models/order_models.dart';
import '../data/orders_gateway.dart';

@immutable
sealed class CancelOrderState {
  const CancelOrderState();
}

class CancelOrderForm extends CancelOrderState {
  const CancelOrderForm({this.reason, this.note});
  final String? reason;
  final String? note;

  CancelOrderForm copyWith({String? reason, String? note}) =>
      CancelOrderForm(reason: reason ?? this.reason, note: note ?? this.note);
}

class CancelOrderSubmitting extends CancelOrderState {
  const CancelOrderSubmitting();
}

class CancelOrderSuccess extends CancelOrderState {
  const CancelOrderSuccess(this.detail);
  final OrderDetail detail;
}

/// 409 conflict — server rejected the cancel because the order moved
/// state under us. Screen shows a banner with Refresh CTA wired to the
/// underlying OrderDetail bloc.
class CancelOrderStaleConflict extends CancelOrderState {
  const CancelOrderStaleConflict();
}

class CancelOrderFailure extends CancelOrderState {
  const CancelOrderFailure({required this.reason});
  final String reason;
}

@immutable
sealed class CancelOrderEvent {
  const CancelOrderEvent();
}

class CancelReasonChanged extends CancelOrderEvent {
  const CancelReasonChanged(this.reason);
  final String reason;
}

class CancelNoteChanged extends CancelOrderEvent {
  const CancelNoteChanged(this.note);
  final String note;
}

class CancelSubmitted extends CancelOrderEvent {
  const CancelSubmitted();
}

class CancelOrderBloc extends Bloc<CancelOrderEvent, CancelOrderState> {
  CancelOrderBloc({required OrdersGateway gateway, required this.orderId})
      : _gateway = gateway,
        super(const CancelOrderForm()) {
    on<CancelReasonChanged>((e, emit) {
      final s = state;
      if (s is CancelOrderForm) emit(s.copyWith(reason: e.reason));
    });
    on<CancelNoteChanged>((e, emit) {
      final s = state;
      if (s is CancelOrderForm) emit(s.copyWith(note: e.note));
    });
    on<CancelSubmitted>(_onSubmit);
  }

  final OrdersGateway _gateway;
  final String orderId;

  Future<void> _onSubmit(
    CancelSubmitted event,
    Emitter<CancelOrderState> emit,
  ) async {
    final s = state;
    if (s is! CancelOrderForm || s.reason == null) return;
    emit(const CancelOrderSubmitting());
    try {
      final detail = await _gateway.cancel(
        orderId: orderId,
        request: CancelOrderRequest(reason: s.reason!, note: s.note),
      );
      emit(CancelOrderSuccess(detail));
    } on Failure catch (f) {
      // The gateway error mapper routes HTTP errors through here;
      // detect a 409-shaped failure code so the screen can branch on
      // the stale state explicitly.
      if (f.code.contains('conflict') || f.code == 'order.stale_state') {
        emit(const CancelOrderStaleConflict());
      } else {
        emit(CancelOrderFailure(reason: f.message));
      }
    } on DioException catch (e) {
      // Defensive: if the error mapper isn't installed the gateway can
      // still surface a raw DioException with a response. Inspect the
      // status code directly.
      if (e.response?.statusCode == 409) {
        emit(const CancelOrderStaleConflict());
      } else {
        emit(CancelOrderFailure(reason: e.message ?? e.toString()));
      }
    } on Object catch (e) {
      emit(CancelOrderFailure(reason: e.toString()));
    }
  }
}
