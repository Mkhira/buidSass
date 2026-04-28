import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/checkout_view_models.dart';

class ShippingQuotePicker extends StatelessWidget {
  const ShippingQuotePicker({
    super.key,
    required this.quotes,
    required this.selectedId,
    required this.onSelected,
  });

  final List<ShippingQuote> quotes;
  final String? selectedId;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: quotes.map((q) {
        return AppListTile(
          title: q.labelKey,
          subtitle: '${q.etaDays}d',
          trailing: selectedId == q.id
              ? const Icon(Icons.check_circle, color: AppColors.primary)
              : null,
          onTap: () => onSelected(q.id),
        );
      }).toList(growable: false),
    );
  }
}
