/// FR-029 + spec 004 validation echoes — best-effort client validation.
class AddressValidators {
  const AddressValidators._();

  /// E.164 phone — `+` followed by 8–15 digits.
  static bool isE164Phone(String input) {
    return RegExp(r'^\+\d{8,15}$').hasMatch(input.trim());
  }

  /// Per-market postal code regex.
  static bool isValidPostalCode({
    required String marketCode,
    required String input,
  }) {
    final code = input.trim();
    switch (marketCode.toLowerCase()) {
      case 'ksa':
        return RegExp(r'^\d{5}$').hasMatch(code);
      case 'eg':
        return RegExp(r'^\d{5}$').hasMatch(code);
    }
    return code.isNotEmpty;
  }

  static String? validatePhoneOrNull(String? value) {
    if (value == null || value.isEmpty) return 'phone.required';
    return isE164Phone(value) ? null : 'phone.invalid_e164';
  }

  static String? validatePostalCodeOrNull({
    required String marketCode,
    required String? value,
  }) {
    if (value == null || value.isEmpty) return 'postal.required';
    return isValidPostalCode(marketCode: marketCode, input: value)
        ? null
        : 'postal.invalid';
  }
}
