import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/legacy_quotations_gateway.dart';
import '../data/models/legacy_quotation_models.dart';

@immutable
sealed class LegacyQuotationsListEvent {
  const LegacyQuotationsListEvent();
}

class LegacyQuotationsListStarted extends LegacyQuotationsListEvent {
  const LegacyQuotationsListStarted();
}

class LegacyQuotationsListRefreshed extends LegacyQuotationsListEvent {
  const LegacyQuotationsListRefreshed();
}

@immutable
sealed class LegacyQuotationsListState {
  const LegacyQuotationsListState();
}

class LegacyQuotationsListLoading extends LegacyQuotationsListState {
  const LegacyQuotationsListLoading();
}

class LegacyQuotationsListEmpty extends LegacyQuotationsListState {
  const LegacyQuotationsListEmpty();
}

class LegacyQuotationsListLoaded extends LegacyQuotationsListState {
  const LegacyQuotationsListLoaded({required this.items});
  final List<LegacyQuotationListItem> items;
}

class LegacyQuotationsListFailure extends LegacyQuotationsListState {
  const LegacyQuotationsListFailure({required this.reason});
  final String reason;
}

/// Bloc for S-8.legacy.1. Gateway returns `[]` on 404 so this bloc
/// just transitions to Empty (the menu entry is hidden by the caller
/// when state is Empty).
class LegacyQuotationsListBloc
    extends Bloc<LegacyQuotationsListEvent, LegacyQuotationsListState> {
  LegacyQuotationsListBloc({required LegacyQuotationsGateway gateway})
      : _gateway = gateway,
        super(const LegacyQuotationsListLoading()) {
    on<LegacyQuotationsListStarted>(_load);
    on<LegacyQuotationsListRefreshed>(_load);
  }

  final LegacyQuotationsGateway _gateway;

  Future<void> _load(
    LegacyQuotationsListEvent e,
    Emitter<LegacyQuotationsListState> emit,
  ) async {
    emit(const LegacyQuotationsListLoading());
    try {
      final items = await _gateway.list();
      if (items.isEmpty) {
        emit(const LegacyQuotationsListEmpty());
        return;
      }
      emit(LegacyQuotationsListLoaded(items: items));
    } on Object catch (_) {
      emit(const LegacyQuotationsListFailure(
        reason: 'legacy_quotations.load_failed',
      ));
    }
  }
}
