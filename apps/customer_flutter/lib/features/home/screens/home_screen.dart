import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/home_bloc.dart';
import '../widgets/banner_carousel.dart';
import '../widgets/category_tiles.dart';
import '../widgets/featured_section.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.navHome)),
      body: BlocBuilder<HomeBloc, HomeState>(
        builder: (context, state) {
          return switch (state) {
            HomeLoading() => LoadingState(semanticsLabel: l10n.commonLoading),
            HomeEmpty() => EmptyState(title: l10n.commonEmpty),
            HomeError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context.read<HomeBloc>().add(const HomeRequested()),
                retryLabel: l10n.commonRetry,
              ),
            HomeLoaded(:final payload) => RefreshIndicator(
                onRefresh: () async => context
                    .read<HomeBloc>()
                    .add(const HomeRefreshRequested()),
                child: ListView(
                  children: [
                    const SizedBox(height: AppSpacing.md),
                    BannerCarousel(banners: payload.banners),
                    const SizedBox(height: AppSpacing.lg),
                    FeaturedSection(
                      titleKey: l10n.homeFeaturedEssentials,
                      items: payload.featured,
                    ),
                    const SizedBox(height: AppSpacing.lg),
                    CategoryTiles(
                      categories: payload.categories,
                      onTap: (c) => context.go('/c/${c.id}'),
                    ),
                  ],
                ),
              ),
          };
        },
      ),
    );
  }
}
