import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../data/catalog_gateway.dart';
import '../data/models/catalog_models.dart';

@immutable
sealed class BrandsListState {
  const BrandsListState();
}

class BrandsListLoading extends BrandsListState {
  const BrandsListLoading();
}

class BrandsListLoaded extends BrandsListState {
  const BrandsListLoaded(this.brands);
  final List<CatalogBrand> brands;
}

class BrandsListEmpty extends BrandsListState {
  const BrandsListEmpty();
}

class BrandsListError extends BrandsListState {
  const BrandsListError(this.failure);
  final Failure failure;
}

@immutable
sealed class BrandsListEvent {
  const BrandsListEvent();
}

class BrandsListRequested extends BrandsListEvent {
  const BrandsListRequested({this.market});
  final String? market;
}

class BrandsListBloc extends Bloc<BrandsListEvent, BrandsListState> {
  BrandsListBloc({
    required CatalogGateway gateway,
    String defaultMarket = 'ksa',
  })  : _gateway = gateway,
        _market = defaultMarket,
        super(const BrandsListLoading()) {
    on<BrandsListRequested>(_onLoad);
  }

  final CatalogGateway _gateway;
  String _market;

  Future<void> _onLoad(
    BrandsListRequested event,
    Emitter<BrandsListState> emit,
  ) async {
    if (event.market != null) _market = event.market!;
    emit(const BrandsListLoading());
    try {
      final result = await _gateway.listBrands(market: _market);
      if (result.isEmpty) {
        emit(const BrandsListEmpty());
      } else {
        emit(BrandsListLoaded(result));
      }
    } on Failure catch (f) {
      emit(BrandsListError(f));
    }
  }
}
