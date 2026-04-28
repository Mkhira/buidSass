import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/cart_view_models.dart';

class CartLineTile extends StatelessWidget {
  const CartLineTile({
    super.key,
    required this.line,
    required this.onQuantity,
    required this.onRemove,
  });

  final CartLineViewModel line;
  final ValueChanged<int> onQuantity;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(width: 56, height: 56, color: AppColors.neutral),
          const SizedBox(width: AppSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(line.name, style: AppTypography.body),
                const SizedBox(height: AppSpacing.xs),
                if (line.restrictedAndUnverified)
                  RestrictedBadge(label: l10n.verificationRequired),
                Row(
                  children: [
                    IconButton(
                      icon: const Icon(Icons.remove),
                      onPressed: line.quantity > 1
                          ? () => onQuantity(line.quantity - 1)
                          : null,
                    ),
                    Text('${line.quantity}'),
                    IconButton(
                      icon: const Icon(Icons.add),
                      onPressed: () => onQuantity(line.quantity + 1),
                    ),
                  ],
                ),
              ],
            ),
          ),
          IconButton(
            icon: const Icon(Icons.delete_outline),
            onPressed: onRemove,
          ),
        ],
      ),
    );
  }
}
