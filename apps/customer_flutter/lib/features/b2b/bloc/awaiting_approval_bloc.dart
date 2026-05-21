import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/quote_models.dart';
import '../data/quotes_gateway.dart';

@immutable
sealed class AwaitingApprovalState {
  const AwaitingApprovalState();
}

class AwaitingApprovalLoading extends AwaitingApprovalState {
  const AwaitingApprovalLoading();
}

class AwaitingApprovalEmpty extends AwaitingApprovalState {
  const AwaitingApprovalEmpty();
}

class AwaitingApprovalLoaded extends AwaitingApprovalState {
  const AwaitingApprovalLoaded({required this.items});
  final List<QuoteListItem> items;
}

class AwaitingApprovalFailure extends AwaitingApprovalState {
  const AwaitingApprovalFailure({required this.reason});
  final String reason;
}

@immutable
sealed class AwaitingApprovalEvent {
  const AwaitingApprovalEvent();
}

class AwaitingApprovalStarted extends AwaitingApprovalEvent {
  const AwaitingApprovalStarted();
}

class AwaitingApprovalRefreshed extends AwaitingApprovalEvent {
  const AwaitingApprovalRefreshed();
}

/// Bloc for S-8.2 — approver-only list. Route guard pre-filters by
/// `Company.isApprover`; this bloc trusts that and surfaces whatever
/// the server returns (server gates by role anyway).
class AwaitingApprovalBloc
    extends Bloc<AwaitingApprovalEvent, AwaitingApprovalState> {
  AwaitingApprovalBloc({required QuotesGateway gateway})
      : _gateway = gateway,
        super(const AwaitingApprovalLoading()) {
    on<AwaitingApprovalStarted>(_load);
    on<AwaitingApprovalRefreshed>(_load);
  }

  final QuotesGateway _gateway;

  Future<void> _load(
    AwaitingApprovalEvent e,
    Emitter<AwaitingApprovalState> emit,
  ) async {
    emit(const AwaitingApprovalLoading());
    try {
      final page = await _gateway.awaitingMyApproval();
      if (page.items.isEmpty) {
        emit(const AwaitingApprovalEmpty());
        return;
      }
      emit(AwaitingApprovalLoaded(items: page.items));
    } on Object catch (err) {
      emit(AwaitingApprovalFailure(reason: err.toString()));
    }
  }
}
