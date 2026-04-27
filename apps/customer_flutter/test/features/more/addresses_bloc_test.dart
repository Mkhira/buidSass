import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/more/bloc/addresses_bloc.dart';
import 'package:customer_flutter/features/more/data/address_view_models.dart';
import 'package:customer_flutter/features/more/data/addresses_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements AddressesRepository {}

AddressViewModel _addr(String id) {
  return AddressViewModel(
    id: id,
    label: 'Home',
    recipient: 'Test',
    line1: '1 St',
    city: 'Riyadh',
    country: 'SA',
    postalCode: '12345',
    marketCode: 'ksa',
    phone: '+966500000000',
  );
}

void main() {
  late _MockRepo repo;
  setUp(() {
    repo = _MockRepo();
    registerFallbackValue(const AddressDraft(
      label: 'l',
      recipient: 'r',
      line1: 'a',
      city: 'c',
      country: 'C',
      postalCode: '12345',
      marketCode: 'ksa',
      phone: '+966500000000',
    ));
  });

  blocTest<AddressesBloc, AddressesState>(
    'Requested -> Empty when list returns empty',
    build: () {
      when(repo.list).thenAnswer((_) async => const <AddressViewModel>[]);
      return AddressesBloc(repository: repo);
    },
    act: (b) => b.add(const AddressesRequested()),
    expect: () => [isA<AddressesLoading>(), isA<AddressesEmpty>()],
  );

  blocTest<AddressesBloc, AddressesState>(
    'Requested -> Loaded on non-empty list',
    build: () {
      when(repo.list).thenAnswer((_) async => [_addr('1')]);
      return AddressesBloc(repository: repo);
    },
    act: (b) => b.add(const AddressesRequested()),
    expect: () => [isA<AddressesLoading>(), isA<AddressesLoaded>()],
  );

  blocTest<AddressesBloc, AddressesState>(
    'list throws -> Error',
    build: () {
      when(repo.list).thenThrow(Exception('boom'));
      return AddressesBloc(repository: repo);
    },
    act: (b) => b.add(const AddressesRequested()),
    expect: () => [isA<AddressesLoading>(), isA<AddressesError>()],
  );
}
