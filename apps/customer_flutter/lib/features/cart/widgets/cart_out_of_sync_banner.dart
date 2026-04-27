import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

class CartOutOfSyncBanner extends StatelessWidget {
  const CartOutOfSyncBanner({super.key, required this.message, this.onReload});
  final String message;
  final VoidCallback? onReload;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(AppSpacing.md),
      color: AppColors.warning.withValues(alpha: 0.1),
      child: Row(
        children: [
          const Icon(Icons.info_outline, color: AppColors.warning),
          const SizedBox(width: AppSpacing.sm),
          Expanded(child: Text(message, style: AppTypography.body)),
          if (onReload != null)
            TextButton(onPressed: onReload, child: const Icon(Icons.refresh)),
        ],
      ),
    );
  }
}
