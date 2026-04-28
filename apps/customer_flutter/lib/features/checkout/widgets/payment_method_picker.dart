import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/checkout_view_models.dart';

class PaymentMethodPicker extends StatelessWidget {
  const PaymentMethodPicker({
    super.key,
    required this.methods,
    required this.selectedId,
    required this.onSelected,
  });

  final List<PaymentMethod> methods;
  final String? selectedId;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: methods.map((m) {
        return AppListTile(
          title: m.labelKey,
          trailing: selectedId == m.id
              ? const Icon(Icons.check_circle, color: AppColors.primary)
              : null,
          onTap: () => onSelected(m.id),
        );
      }).toList(growable: false),
    );
  }
}
