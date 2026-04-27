import 'package:customer_flutter/features/more/services/address_validators.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('isE164Phone', () {
    test('accepts +<country><digits>', () {
      expect(AddressValidators.isE164Phone('+966500000000'), isTrue);
      expect(AddressValidators.isE164Phone('+201000000000'), isTrue);
    });

    test('rejects missing +', () {
      expect(AddressValidators.isE164Phone('966500000000'), isFalse);
    });

    test('rejects too few digits', () {
      expect(AddressValidators.isE164Phone('+96650'), isFalse);
    });
  });

  group('isValidPostalCode', () {
    test('ksa requires 5 digits', () {
      expect(
          AddressValidators.isValidPostalCode(
              marketCode: 'ksa', input: '12345'),
          isTrue);
      expect(
          AddressValidators.isValidPostalCode(marketCode: 'ksa', input: '1234'),
          isFalse);
    });

    test('eg requires 5 digits', () {
      expect(
          AddressValidators.isValidPostalCode(marketCode: 'eg', input: '12345'),
          isTrue);
      expect(
          AddressValidators.isValidPostalCode(marketCode: 'eg', input: 'abcde'),
          isFalse);
    });

    test('unknown market accepts any non-empty', () {
      expect(
          AddressValidators.isValidPostalCode(marketCode: 'us', input: 'A1B'),
          isTrue);
      expect(AddressValidators.isValidPostalCode(marketCode: 'us', input: ''),
          isFalse);
    });
  });

  group('validateOrNull helpers', () {
    test('validatePhoneOrNull returns reason or null', () {
      expect(AddressValidators.validatePhoneOrNull(null), 'phone.required');
      expect(AddressValidators.validatePhoneOrNull(''), 'phone.required');
      expect(
          AddressValidators.validatePhoneOrNull('123'), 'phone.invalid_e164');
      expect(AddressValidators.validatePhoneOrNull('+966500000000'), isNull);
    });

    test('validatePostalCodeOrNull returns reason or null', () {
      expect(
          AddressValidators.validatePostalCodeOrNull(
              marketCode: 'ksa', value: null),
          'postal.required');
      expect(
          AddressValidators.validatePostalCodeOrNull(
              marketCode: 'ksa', value: '999'),
          'postal.invalid');
      expect(
          AddressValidators.validatePostalCodeOrNull(
              marketCode: 'ksa', value: '12345'),
          isNull);
    });
  });
}
