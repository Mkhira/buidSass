import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../bloc/product_list_bloc.dart';
import '../data/models/catalog_models.dart';
import '../widgets/filter_bar.dart';
import '../widgets/product_card.dart';

/// Shared screen used by:
///   * S-2.3 (category detail) — `categorySlug` filter only
///   * S-2.5 (brand product list) — `categorySlug='all'` + `brandSlug` filter
///
/// Pagination via append-on-scroll. Pull-to-refresh resets to page 1
/// and clears the in-bloc state (per Phase 2 S-2.3 acceptance criteria).
class ProductListScreen extends StatelessWidget {
  const ProductListScreen({
    super.key,
    required this.locale,
    required this.title,
    required this.onProductTap,
    required this.onAddToCart,
    required this.onRequestVerification,
    this.brands = const [],
    this.showBrandOnCards = true,
  });

  final String locale;
  final String title;
  final ValueChanged<CatalogProduct> onProductTap;
  final ValueChanged<CatalogProduct> onAddToCart;
  final ValueChanged<CatalogProduct> onRequestVerification;
  final List<CatalogBrand> brands;
  final bool showBrandOnCards;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(title)),
      body: BlocBuilder<ProductListBloc, ProductListState>(
        builder: (context, state) {
          if (state.loadingInitial) {
            return const Center(child: CircularProgressIndicator());
          }
          if (state.failure != null && state.items.isEmpty) {
            return _ErrorView(
              failure: state.failure!,
              onRetry: () => context
                  .read<ProductListBloc>()
                  .add(const ProductListRefreshed()),
            );
          }
          if (state.isEmpty) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(AppSpacing.md),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.inventory_2_outlined,
                        size: 32, color: AppColors.textSecondary),
                    const SizedBox(height: AppSpacing.sm),
                    const Text('No products match these filters'),
                    const SizedBox(height: AppSpacing.sm),
                    FilledButton(
                      onPressed: () => context.read<ProductListBloc>().add(
                            const ProductListBrandChanged(null),
                          ),
                      child: const Text('Clear filters'),
                    ),
                  ],
                ),
              ),
            );
          }
          return Column(
            children: [
              FilterBar(
                sort: state.query.sort,
                locale: locale,
                onSortChanged: (s) => context
                    .read<ProductListBloc>()
                    .add(ProductListSortChanged(s)),
                brands: brands,
                selectedBrandSlug: state.query.brandSlug,
                onBrandChanged: brands.isEmpty
                    ? null
                    : (slug) => context
                        .read<ProductListBloc>()
                        .add(ProductListBrandChanged(slug)),
              ),
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () async => context
                      .read<ProductListBloc>()
                      .add(const ProductListRefreshed()),
                  child: NotificationListener<ScrollNotification>(
                    onNotification: (n) {
                      if (n.metrics.pixels >=
                              n.metrics.maxScrollExtent - 240 &&
                          state.hasMore &&
                          !state.loadingMore) {
                        context
                            .read<ProductListBloc>()
                            .add(const ProductListLoadMore());
                      }
                      return false;
                    },
                    child: GridView.builder(
                      padding: const EdgeInsets.all(AppSpacing.md),
                      itemCount:
                          state.items.length + (state.loadingMore ? 1 : 0),
                      gridDelegate:
                          const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 2,
                        mainAxisSpacing: AppSpacing.sm,
                        crossAxisSpacing: AppSpacing.sm,
                        childAspectRatio: 0.62,
                      ),
                      itemBuilder: (_, i) {
                        if (i >= state.items.length) {
                          return const Center(
                            child: CircularProgressIndicator(),
                          );
                        }
                        final p = state.items[i];
                        return ProductCard(
                          product: p,
                          locale: locale,
                          availability: state.availability[p.id],
                          aggregate: state.aggregates[p.id],
                          showBrand: showBrandOnCards,
                          onTap: () => onProductTap(p),
                          onAddToCart: () => onAddToCart(p),
                          onRequestVerification: () => onRequestVerification(p),
                        );
                      },
                    ),
                  ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _ErrorView extends StatelessWidget {
  const _ErrorView({required this.failure, required this.onRetry});
  final Object failure;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline,
                size: 32, color: AppColors.danger),
            const SizedBox(height: AppSpacing.sm),
            const Text('Failed to load products.'),
            const SizedBox(height: AppSpacing.sm),
            FilledButton(onPressed: onRetry, child: const Text('Retry')),
          ],
        ),
      ),
    );
  }
}
