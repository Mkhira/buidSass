import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/catalog_view_models.dart';

class ProductGridTile extends StatelessWidget {
  const ProductGridTile({super.key, required this.item, this.onTap});

  final ProductListingItem item;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return InkWell(
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AspectRatio(
            aspectRatio: 1,
            child: Container(
              decoration: BoxDecoration(
                color: AppColors.neutral,
                borderRadius: BorderRadius.circular(8),
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          Text(
            item.name,
            style: AppTypography.body,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
          ),
          const SizedBox(height: AppSpacing.xs),
          if (item.isRestricted)
            RestrictedBadge(label: l10n.verificationRequired),
          if (!item.inStock)
            Text(l10n.stockOutOfStock,
                style: AppTypography.caption.copyWith(color: AppColors.danger)),
        ],
      ),
    );
  }
}
