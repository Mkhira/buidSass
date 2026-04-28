import 'package:customer_flutter/core/localization/formatters.dart';
import 'package:customer_flutter/core/market/market_resolver.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';

void main() {
  setUpAll(() async {
    // intl's DateFormat requires per-locale data initialised in tests.
    await initializeDateFormatting('en');
    await initializeDateFormatting('ar');
  });

  test('currencyFromMinor renders KSA in SAR', () {
    final s = CustomerFormatters.currencyFromMinor(
      minorAmount: 12345,
      market: Market.ksa,
      localeCode: 'en',
    );
    expect(s, contains('SAR'));
    expect(s, contains('123.45'));
  });

  test('currencyFromMinor renders EG in EGP', () {
    final s = CustomerFormatters.currencyFromMinor(
      minorAmount: 99900,
      market: Market.eg,
      localeCode: 'en',
    );
    expect(s, contains('EGP'));
  });

  test('quantity formats with locale grouping', () {
    expect(
      CustomerFormatters.quantity(value: 1234567, localeCode: 'en'),
      contains('1,234,567'),
    );
  });

  test('date renders in the requested locale', () {
    final result = CustomerFormatters.date(
      value: DateTime.utc(2026, 4, 28),
      localeCode: 'en',
    );
    expect(result, contains('2026'));
  });

  test('dateTime renders date and time', () {
    final result = CustomerFormatters.dateTime(
      value: DateTime.utc(2026, 4, 28, 9, 30),
      localeCode: 'en',
    );
    expect(result, contains('2026'));
    expect(result.length, greaterThan(10));
  });
}
