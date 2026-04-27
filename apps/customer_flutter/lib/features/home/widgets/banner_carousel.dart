import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/home_view_models.dart';

class BannerCarousel extends StatelessWidget {
  const BannerCarousel({super.key, required this.banners, this.onBannerTap});

  final List<HomeBannerViewModel> banners;
  final ValueChanged<HomeBannerViewModel>? onBannerTap;

  @override
  Widget build(BuildContext context) {
    if (banners.isEmpty) return const SizedBox.shrink();
    return SizedBox(
      height: 160,
      child: PageView.builder(
        itemCount: banners.length,
        itemBuilder: (ctx, i) {
          final b = banners[i];
          return Semantics(
            label: b.titleKey,
            button: true,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
              child: GestureDetector(
                onTap: () => onBannerTap?.call(b),
                child: Container(
                  decoration: BoxDecoration(
                    color: AppColors.primary,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  alignment: AlignmentDirectional.centerStart,
                  padding: const EdgeInsets.all(AppSpacing.lg),
                  child: Text(
                    b.titleKey,
                    style: AppTypography.headline.copyWith(color: Colors.white),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}
