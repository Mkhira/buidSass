import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/home_repository.dart';
import '../data/home_view_models.dart';

@immutable
sealed class HomeState {
  const HomeState();
}

class HomeLoading extends HomeState {
  const HomeLoading();
}

class HomeEmpty extends HomeState {
  const HomeEmpty();
}

class HomeLoaded extends HomeState {
  const HomeLoaded(this.payload);
  final HomePayloadViewModel payload;
}

class HomeError extends HomeState {
  const HomeError(this.reason);
  final String reason;
}

@immutable
sealed class HomeEvent {
  const HomeEvent();
}

class HomeRequested extends HomeEvent {
  const HomeRequested();
}

class HomeRefreshRequested extends HomeEvent {
  const HomeRefreshRequested();
}

class HomeBloc extends Bloc<HomeEvent, HomeState> {
  HomeBloc({required HomeRepository repository})
      : _repository = repository,
        super(const HomeLoading()) {
    on<HomeRequested>(_onLoad);
    on<HomeRefreshRequested>(_onLoad);
  }

  final HomeRepository _repository;

  Future<void> _onLoad(HomeEvent event, Emitter<HomeState> emit) async {
    if (event is HomeRefreshRequested && state is! HomeError) {
      // refresh keeps the previous state visible
    } else {
      emit(const HomeLoading());
    }
    try {
      final payload = await _repository.fetchHome();
      if (payload.isEmpty) {
        emit(const HomeEmpty());
      } else {
        emit(HomeLoaded(payload));
      }
    } on Object catch (e) {
      emit(HomeError(e.toString()));
    }
  }
}
