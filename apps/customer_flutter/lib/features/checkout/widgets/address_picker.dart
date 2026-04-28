import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/checkout_view_models.dart';

class AddressPicker extends StatelessWidget {
  const AddressPicker({
    super.key,
    required this.addresses,
    required this.selectedId,
    required this.onSelected,
  });

  final List<CheckoutAddress> addresses;
  final String? selectedId;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: addresses.map((a) {
        return AppListTile(
          title: a.label,
          subtitle: '${a.line1}, ${a.city}',
          trailing: selectedId == a.id
              ? const Icon(Icons.check_circle, color: AppColors.primary)
              : null,
          onTap: () => onSelected(a.id),
        );
      }).toList(growable: false),
    );
  }
}
