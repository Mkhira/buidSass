import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../data/models/catalog_models.dart';

/// Renders a [CatalogMoney] consistently across cards, PDP, and price
/// breakdowns. Locale-aware via [NumberFormat.currency] — Arabic gets
/// Arabic-Indic digits and right-side currency placement; other locales
/// get their native separators. Currency-decimal scaling honours
/// [fractionDigitsForCurrency] so JPY renders 0-decimal and KWD renders
/// 3-decimal correctly.
///
/// All catalog prices are presented through this widget so a price format
/// change lands in one place.
class PriceLabel extends StatelessWidget {
  const PriceLabel({
    super.key,
    required this.money,
    this.style,
    this.strikethrough = false,
    this.semanticLabel,
    this.locale,
  });

  final CatalogMoney money;
  final TextStyle? style;

  /// True when this is the original price next to a discounted price.
  final bool strikethrough;

  /// Override the locale used for formatting. When null, the widget
  /// resolves it from [Localizations.localeOf] so AR screens render with
  /// Arabic-Indic digits.
  final String? semanticLabel;
  final String? locale;

  @override
  Widget build(BuildContext context) {
    final resolvedLocale =
        locale ?? Localizations.localeOf(context).toLanguageTag();
    final digits = fractionDigitsForCurrency(money.currency);
    final formatter = NumberFormat.currency(
      locale: resolvedLocale,
      name: money.currency,
      decimalDigits: digits,
    );
    final asMajor = money.amountMinor / _pow10Int(digits);
    final text = formatter.format(asMajor);
    final base = style ?? Theme.of(context).textTheme.titleMedium;
    final resolved = strikethrough
        ? (base ?? const TextStyle()).copyWith(
            decoration: TextDecoration.lineThrough,
            color: Theme.of(context).hintColor,
          )
        : base;
    return Semantics(
      label: semanticLabel ?? text,
      child: Text(text, style: resolved),
    );
  }
}

int _pow10Int(int n) {
  var r = 1;
  for (var i = 0; i < n; i++) {
    r *= 10;
  }
  return r;
}
