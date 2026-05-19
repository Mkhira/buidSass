import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../bloc/categories_list_bloc.dart';
import '../data/models/catalog_models.dart';

/// S-2.2 — categories list. Two-column grid of category tiles with
/// optional icons; tap routes to `/categories/{slug}` (category detail =
/// S-2.3 product list).
class CategoriesListScreen extends StatelessWidget {
  const CategoriesListScreen({
    super.key,
    required this.locale,
    required this.onCategoryTap,
    this.copy = const CategoriesListCopy(),
  });

  final String locale;
  final ValueChanged<CatalogCategory> onCategoryTap;

  /// Locale-resolved screen copy. Defaults are English placeholders; the
  /// router/composition layer passes locale-resolved strings.
  final CategoriesListCopy copy;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(copy.title)),
      body: BlocBuilder<CategoriesListBloc, CategoriesListState>(
        builder: (context, state) {
          return switch (state) {
            CategoriesListLoading() => const Center(
                child: CircularProgressIndicator(),
              ),
            CategoriesListEmpty() => Center(child: Text(copy.empty)),
            CategoriesListError(:final failure) => Center(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.error_outline,
                          size: 32, color: AppColors.danger),
                      const SizedBox(height: AppSpacing.sm),
                      Text(
                        '${copy.failedToLoad} (${failure.correlationIdShort})',
                      ),
                      const SizedBox(height: AppSpacing.sm),
                      FilledButton(
                        onPressed: () => context
                            .read<CategoriesListBloc>()
                            .add(const CategoriesListRequested()),
                        child: Text(copy.retry),
                      ),
                    ],
                  ),
                ),
              ),
            CategoriesListLoaded(:final categories) => RefreshIndicator(
                onRefresh: () async => context
                    .read<CategoriesListBloc>()
                    .add(const CategoriesListRequested()),
                child: GridView.builder(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  itemCount: categories.length,
                  gridDelegate:
                      const SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: 2,
                    mainAxisSpacing: AppSpacing.sm,
                    crossAxisSpacing: AppSpacing.sm,
                    childAspectRatio: 1.3,
                  ),
                  itemBuilder: (_, i) {
                    final c = categories[i];
                    return _CategoryTile(
                      category: c,
                      locale: locale,
                      onTap: () => onCategoryTap(c),
                    );
                  },
                ),
              ),
          };
        },
      ),
    );
  }
}

class _CategoryTile extends StatelessWidget {
  const _CategoryTile({
    required this.category,
    required this.locale,
    required this.onTap,
  });

  final CatalogCategory category;
  final String locale;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.sm),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (category.iconUrl != null && category.iconUrl!.isNotEmpty)
                Expanded(
                  child: Image.network(
                    category.iconUrl!,
                    errorBuilder: (_, __, ___) => const Icon(
                      Icons.category_outlined,
                      size: 32,
                      color: AppColors.textSecondary,
                    ),
                  ),
                )
              else
                const Expanded(
                  child: Icon(
                    Icons.category_outlined,
                    size: 32,
                    color: AppColors.textSecondary,
                  ),
                ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                category.name.resolve(locale),
                textAlign: TextAlign.center,
                style: const TextStyle(fontWeight: FontWeight.w600),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Locale-resolved screen copy. Defaults are English placeholders for
/// test + initial wiring; the screen layer passes localized strings.
class CategoriesListCopy {
  const CategoriesListCopy({
    this.title = 'Categories',
    this.empty = 'No categories',
    this.failedToLoad = 'Failed to load.',
    this.retry = 'Retry',
  });

  final String title;
  final String empty;
  final String failedToLoad;
  final String retry;
}
