import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/verification_models.dart';
import '../data/verification_gateway.dart';

@immutable
sealed class VerificationListState {
  const VerificationListState();
}

class VerificationListLoading extends VerificationListState {
  const VerificationListLoading();
}

class VerificationListLoaded extends VerificationListState {
  const VerificationListLoaded({required this.active, required this.items});
  final VerificationActive active;
  final List<VerificationListItem> items;

  bool get hasAny => items.isNotEmpty || active.hasCase;
}

class VerificationListFailure extends VerificationListState {
  const VerificationListFailure({required this.reason});
  final String reason;
}

@immutable
sealed class VerificationListEvent {
  const VerificationListEvent();
}

class VerificationListStarted extends VerificationListEvent {
  const VerificationListStarted();
}

class VerificationListRefreshed extends VerificationListEvent {
  const VerificationListRefreshed();
}

/// Bloc for S-7.1 verification list. Fetches the active banner and the
/// history list in parallel on mount + pull-to-refresh.
class VerificationListBloc
    extends Bloc<VerificationListEvent, VerificationListState> {
  VerificationListBloc({required VerificationGateway gateway})
      : _gateway = gateway,
        super(const VerificationListLoading()) {
    on<VerificationListStarted>(_load);
    on<VerificationListRefreshed>(_load);
  }

  final VerificationGateway _gateway;

  Future<void> _load(
    VerificationListEvent event,
    Emitter<VerificationListState> emit,
  ) async {
    emit(const VerificationListLoading());
    try {
      final results = await Future.wait<Object>([
        _gateway.getActive(),
        _gateway.list(),
      ]);
      final active = results[0] as VerificationActive;
      final page = results[1] as VerificationListPage;
      emit(VerificationListLoaded(active: active, items: page.items));
    } on Object catch (e) {
      emit(VerificationListFailure(reason: e.toString()));
    }
  }
}
