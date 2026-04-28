import 'address_view_models.dart';

abstract class AddressesRepository {
  Future<List<AddressViewModel>> list();
  Future<AddressViewModel> create(AddressDraft draft);
  Future<AddressViewModel> update({
    required String id,
    required AddressDraft draft,
  });
  Future<void> delete(String id);
  Future<AddressViewModel> setDefault(String id);
}

class StubAddressesRepository implements AddressesRepository {
  @override
  Future<List<AddressViewModel>> list() async => const <AddressViewModel>[];

  @override
  Future<AddressViewModel> create(AddressDraft draft) async {
    throw const AddressesGapException();
  }

  @override
  Future<AddressViewModel> update({
    required String id,
    required AddressDraft draft,
  }) async {
    throw const AddressesGapException();
  }

  @override
  Future<void> delete(String id) async {
    throw const AddressesGapException();
  }

  @override
  Future<AddressViewModel> setDefault(String id) async {
    throw const AddressesGapException();
  }
}

class AddressesGapException implements Exception {
  const AddressesGapException();
  @override
  String toString() => 'Addresses client gap — escalate to spec 004 (FR-031).';
}
