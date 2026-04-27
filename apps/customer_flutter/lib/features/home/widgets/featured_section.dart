import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/home_view_models.dart';

class FeaturedSection extends StatelessWidget {
  const FeaturedSection({
    super.key,
    required this.titleKey,
    required this.items,
    this.onItemTap,
  });

  final String titleKey;
  final List<FeaturedProductViewModel> items;
  final ValueChanged<FeaturedProductViewModel>? onItemTap;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
          child: Text(titleKey, style: AppTypography.headline),
        ),
        const SizedBox(height: AppSpacing.sm),
        SizedBox(
          height: 180,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsetsDirectional.symmetric(horizontal: AppSpacing.md),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(width: AppSpacing.sm),
            itemBuilder: (ctx, i) {
              final p = items[i];
              return SizedBox(
                width: 140,
                child: GestureDetector(
                  onTap: () => onItemTap?.call(p),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        height: 120,
                        decoration: BoxDecoration(
                          color: AppColors.neutral,
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                      const SizedBox(height: AppSpacing.sm),
                      Text(
                        p.name,
                        style: AppTypography.body,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}
