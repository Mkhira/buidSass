import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/catalog_repository.dart';
import '../data/catalog_view_models.dart';

@immutable
sealed class ProductDetailState {
  const ProductDetailState();
}

class ProductDetailLoading extends ProductDetailState {
  const ProductDetailLoading();
}

class ProductDetailLoaded extends ProductDetailState {
  const ProductDetailLoaded(this.product);
  final ProductDetailViewModel product;
}

class ProductDetailRestricted extends ProductDetailState {
  const ProductDetailRestricted(this.product);
  final ProductDetailViewModel product;
}

class ProductDetailOutOfStock extends ProductDetailState {
  const ProductDetailOutOfStock(this.product);
  final ProductDetailViewModel product;
}

class ProductDetailError extends ProductDetailState {
  const ProductDetailError(this.reason);
  final String reason;
}

@immutable
sealed class ProductDetailEvent {
  const ProductDetailEvent();
}

class ProductRequested extends ProductDetailEvent {
  const ProductRequested(this.productId);
  final String productId;
}

class ProductDetailBloc extends Bloc<ProductDetailEvent, ProductDetailState> {
  ProductDetailBloc({
    required CatalogRepository repository,
    required this.isCustomerVerified,
  })  : _repository = repository,
        super(const ProductDetailLoading()) {
    on<ProductRequested>(_onLoad);
  }

  final CatalogRepository _repository;
  final bool isCustomerVerified;

  Future<void> _onLoad(
    ProductRequested event,
    Emitter<ProductDetailState> emit,
  ) async {
    emit(const ProductDetailLoading());
    try {
      final product = await _repository.fetchDetail(event.productId);
      if (product.stockSignal.isOutOfStock) {
        emit(ProductDetailOutOfStock(product));
        return;
      }
      if (product.isRestricted && !isCustomerVerified) {
        emit(ProductDetailRestricted(product));
        return;
      }
      emit(ProductDetailLoaded(product));
    } on Object catch (e) {
      emit(ProductDetailError(e.toString()));
    }
  }
}
