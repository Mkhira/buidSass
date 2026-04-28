import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/address_view_models.dart';
import '../data/addresses_repository.dart';

@immutable
sealed class AddressesState {
  const AddressesState();
}

class AddressesLoading extends AddressesState {
  const AddressesLoading();
}

class AddressesLoaded extends AddressesState {
  const AddressesLoaded(this.addresses);
  final List<AddressViewModel> addresses;
}

class AddressesEmpty extends AddressesState {
  const AddressesEmpty();
}

class AddressesError extends AddressesState {
  const AddressesError(this.reason);
  final String reason;
}

@immutable
sealed class AddressesEvent {
  const AddressesEvent();
}

class AddressesRequested extends AddressesEvent {
  const AddressesRequested();
}

class AddressCreated extends AddressesEvent {
  const AddressCreated(this.draft);
  final AddressDraft draft;
}

class AddressUpdated extends AddressesEvent {
  const AddressUpdated({required this.id, required this.draft});
  final String id;
  final AddressDraft draft;
}

class AddressDeleted extends AddressesEvent {
  const AddressDeleted(this.id);
  final String id;
}

class AddressMadeDefault extends AddressesEvent {
  const AddressMadeDefault(this.id);
  final String id;
}

class AddressesBloc extends Bloc<AddressesEvent, AddressesState> {
  AddressesBloc({required AddressesRepository repository})
      : _repository = repository,
        super(const AddressesLoading()) {
    on<AddressesRequested>(_onRequested);
    on<AddressCreated>(_onCreated);
    on<AddressUpdated>(_onUpdated);
    on<AddressDeleted>(_onDeleted);
    on<AddressMadeDefault>(_onMadeDefault);
  }

  final AddressesRepository _repository;

  Future<void> _onRequested(
    AddressesRequested event,
    Emitter<AddressesState> emit,
  ) async {
    emit(const AddressesLoading());
    try {
      final list = await _repository.list();
      emit(list.isEmpty ? const AddressesEmpty() : AddressesLoaded(list));
    } on Object catch (e) {
      emit(AddressesError(e.toString()));
    }
  }

  Future<void> _onCreated(
    AddressCreated event,
    Emitter<AddressesState> emit,
  ) async {
    try {
      await _repository.create(event.draft);
      add(const AddressesRequested());
    } on Object catch (e) {
      emit(AddressesError(e.toString()));
    }
  }

  Future<void> _onUpdated(
    AddressUpdated event,
    Emitter<AddressesState> emit,
  ) async {
    try {
      await _repository.update(id: event.id, draft: event.draft);
      add(const AddressesRequested());
    } on Object catch (e) {
      emit(AddressesError(e.toString()));
    }
  }

  Future<void> _onDeleted(
    AddressDeleted event,
    Emitter<AddressesState> emit,
  ) async {
    try {
      await _repository.delete(event.id);
      add(const AddressesRequested());
    } on Object catch (e) {
      emit(AddressesError(e.toString()));
    }
  }

  Future<void> _onMadeDefault(
    AddressMadeDefault event,
    Emitter<AddressesState> emit,
  ) async {
    try {
      await _repository.setDefault(event.id);
      add(const AddressesRequested());
    } on Object catch (e) {
      emit(AddressesError(e.toString()));
    }
  }
}
