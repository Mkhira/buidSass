import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/return_models.dart';
import '../data/returns_gateway.dart';

@immutable
sealed class ReturnDetailState {
  const ReturnDetailState();
}

class ReturnDetailLoading extends ReturnDetailState {
  const ReturnDetailLoading();
}

class ReturnDetailLoaded extends ReturnDetailState {
  const ReturnDetailLoaded({required this.detail});
  final ReturnDetail detail;
}

class ReturnDetailFailure extends ReturnDetailState {
  const ReturnDetailFailure({required this.reason});
  final String reason;
}

@immutable
sealed class ReturnDetailEvent {
  const ReturnDetailEvent();
}

class ReturnDetailStarted extends ReturnDetailEvent {
  const ReturnDetailStarted();
}

class ReturnDetailRefreshed extends ReturnDetailEvent {
  const ReturnDetailRefreshed();
}

/// Bloc for S-6.3 return detail. Standard load/refresh pattern.
class ReturnDetailBloc extends Bloc<ReturnDetailEvent, ReturnDetailState> {
  ReturnDetailBloc({required ReturnsGateway gateway, required this.returnId})
      : _gateway = gateway,
        super(const ReturnDetailLoading()) {
    on<ReturnDetailStarted>(_load);
    on<ReturnDetailRefreshed>(_load);
  }

  final ReturnsGateway _gateway;
  final String returnId;

  Future<void> _load(
    ReturnDetailEvent event,
    Emitter<ReturnDetailState> emit,
  ) async {
    emit(const ReturnDetailLoading());
    try {
      final detail = await _gateway.getById(returnId);
      emit(ReturnDetailLoaded(detail: detail));
    } on Object catch (e) {
      emit(ReturnDetailFailure(reason: e.toString()));
    }
  }
}
