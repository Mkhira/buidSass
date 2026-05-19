import 'package:flutter/material.dart';

import '../data/models/catalog_models.dart';

/// Renders a [CatalogMoney] consistently across cards, PDP, and price
/// breakdowns. AR locales place the currency suffix to the right of the
/// number (still RTL — `Directionality` ancestor handles the bidi run).
///
/// All catalog prices are presented through this widget so a price format
/// change (e.g. minor-unit policy, separator) lands in one place.
class PriceLabel extends StatelessWidget {
  const PriceLabel({
    super.key,
    required this.money,
    this.style,
    this.strikethrough = false,
    this.semanticLabel,
  });

  final CatalogMoney money;
  final TextStyle? style;

  /// True when this is the original price next to a discounted price.
  final bool strikethrough;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final whole = money.amountMinor ~/ 100;
    final fraction = (money.amountMinor % 100).toString().padLeft(2, '0');
    final text = '$whole.$fraction ${money.currency}'.trim();
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
