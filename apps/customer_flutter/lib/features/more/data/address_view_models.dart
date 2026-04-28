import 'package:flutter/foundation.dart';

@immutable
class AddressViewModel {
  const AddressViewModel({
    required this.id,
    required this.label,
    required this.recipient,
    required this.line1,
    this.line2,
    required this.city,
    this.region,
    required this.country,
    required this.postalCode,
    required this.marketCode,
    required this.phone,
    this.isDefault = false,
  });
  final String id;
  final String label;
  final String recipient;
  final String line1;
  final String? line2;
  final String city;
  final String? region;
  final String country;
  final String postalCode;
  final String marketCode;
  final String phone;
  final bool isDefault;
}

@immutable
class AddressDraft {
  const AddressDraft({
    required this.label,
    required this.recipient,
    required this.line1,
    this.line2,
    required this.city,
    this.region,
    required this.country,
    required this.postalCode,
    required this.marketCode,
    required this.phone,
    this.isDefault = false,
  });
  final String label;
  final String recipient;
  final String line1;
  final String? line2;
  final String city;
  final String? region;
  final String country;
  final String postalCode;
  final String marketCode;
  final String phone;
  final bool isDefault;
}
