import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/home_view_models.dart';

class CategoryTiles extends StatelessWidget {
  const CategoryTiles({super.key, required this.categories, this.onTap});

  final List<CategoryTileViewModel> categories;
  final ValueChanged<CategoryTileViewModel>? onTap;

  @override
  Widget build(BuildContext context) {
    if (categories.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: GridView.builder(
        physics: const NeverScrollableScrollPhysics(),
        shrinkWrap: true,
        itemCount: categories.length,
        gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
          crossAxisCount: 2,
          mainAxisSpacing: AppSpacing.sm,
          crossAxisSpacing: AppSpacing.sm,
          childAspectRatio: 2.4,
        ),
        itemBuilder: (ctx, i) {
          final c = categories[i];
          return InkWell(
            onTap: () => onTap?.call(c),
            child: Container(
              decoration: BoxDecoration(
                color: AppColors.neutral,
                borderRadius: BorderRadius.circular(8),
              ),
              alignment: AlignmentDirectional.center,
              child: Text(c.labelKey, style: AppTypography.body),
            ),
          );
        },
      ),
    );
  }
}
