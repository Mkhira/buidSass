import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../data/catalog_gateway.dart';
import '../data/models/catalog_models.dart';

@immutable
sealed class CategoriesListState {
  const CategoriesListState();
}

class CategoriesListLoading extends CategoriesListState {
  const CategoriesListLoading();
}

class CategoriesListLoaded extends CategoriesListState {
  const CategoriesListLoaded(this.categories);
  final List<CatalogCategory> categories;
}

class CategoriesListEmpty extends CategoriesListState {
  const CategoriesListEmpty();
}

class CategoriesListError extends CategoriesListState {
  const CategoriesListError(this.failure);
  final Failure failure;
}

@immutable
sealed class CategoriesListEvent {
  const CategoriesListEvent();
}

class CategoriesListRequested extends CategoriesListEvent {
  const CategoriesListRequested({this.market});
  final String? market;
}

class CategoriesListBloc
    extends Bloc<CategoriesListEvent, CategoriesListState> {
  CategoriesListBloc({
    required CatalogGateway gateway,
    String defaultMarket = 'ksa',
  })  : _gateway = gateway,
        _market = defaultMarket,
        super(const CategoriesListLoading()) {
    on<CategoriesListRequested>(_onLoad);
  }

  final CatalogGateway _gateway;
  String _market;

  Future<void> _onLoad(
    CategoriesListRequested event,
    Emitter<CategoriesListState> emit,
  ) async {
    if (event.market != null) _market = event.market!;
    emit(const CategoriesListLoading());
    try {
      final result = await _gateway.listCategories(market: _market);
      if (result.isEmpty) {
        emit(const CategoriesListEmpty());
      } else {
        emit(CategoriesListLoaded(result));
      }
    } on Failure catch (f) {
      emit(CategoriesListError(f));
    }
  }
}
