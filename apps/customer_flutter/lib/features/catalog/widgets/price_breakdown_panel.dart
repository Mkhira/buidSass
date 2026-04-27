import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../data/catalog_view_models.dart';

class PriceBreakdownPanel extends StatelessWidget {
  const PriceBreakdownPanel(
      {super.key, required this.breakdown, this.localeCode});
  final PriceBreakdown breakdown;
  final String? localeCode;

  String _format(int minor) {
    final f = NumberFormat.currency(
      locale: localeCode ?? 'en',
      symbol: breakdown.currency,
      decimalDigits: 2,
    );
    return f.format(minor / 100);
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _row('unit', _format(breakdown.unitPriceMinor)),
          if (breakdown.discountMinor > 0)
            _row('discount', '- ${_format(breakdown.discountMinor)}'),
          _row('tax', _format(breakdown.taxMinor)),
          const Divider(),
          _row('total', _format(breakdown.totalMinor), isBold: true),
        ],
      ),
    );
  }

  Widget _row(String label, String value, {bool isBold = false}) {
    final style = isBold
        ? AppTypography.body.copyWith(fontWeight: FontWeight.w700)
        : AppTypography.body;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(child: Text(label, style: style)),
          Text(value, style: style),
        ],
      ),
    );
  }
}
