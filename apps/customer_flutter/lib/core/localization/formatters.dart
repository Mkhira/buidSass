// T097 — per-market currency + numeral formatting.
//
// Single source of truth for amount / quantity / date formatting in
// the customer app. KSA → SAR, EG → EGP per ADR-010. Western Arabic
// numerals (0123456789) by default per research §R4.

import 'package:intl/intl.dart';

import '../market/market_resolver.dart';

class CustomerFormatters {
  const CustomerFormatters._();

  /// Format a minor-units amount (`halalas` / `piasters`) as a localized
  /// currency string. Uses the explicit `Market.currency` symbol so the
  /// rendered string never says SAR for an EG order or vice versa.
  static String currencyFromMinor({
    required int minorAmount,
    required Market market,
    required String localeCode,
  }) {
    final formatter = NumberFormat.currency(
      locale: localeCode,
      symbol: market.currency,
      decimalDigits: 2,
    );
    return formatter.format(minorAmount / 100);
  }

  /// Format a quantity (whole number) — locale-aware grouping.
  static String quantity({
    required int value,
    required String localeCode,
  }) {
    return NumberFormat.decimalPattern(localeCode).format(value);
  }

  /// Format a date in the user's locale + Gregorian calendar.
  /// Hijri parallel rendering is owned by spec 012 (tax invoices).
  static String date({
    required DateTime value,
    required String localeCode,
  }) {
    return DateFormat.yMMMd(localeCode).format(value);
  }

  static String dateTime({
    required DateTime value,
    required String localeCode,
  }) {
    return DateFormat.yMMMd(localeCode).add_jm().format(value);
  }
}
