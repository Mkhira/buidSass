import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../bloc/product_detail_v2_bloc.dart';
import '../data/models/catalog_models.dart';
import '../widgets/price_label.dart';
import '../widgets/rating_block.dart';
import '../widgets/restriction_gate.dart';
import '../widgets/stock_badge.dart';

/// S-2.6 PDP — renders the four-call orchestration from
/// [ProductDetailV2Bloc]. Product shell appears immediately on
/// `product/{slug}` resolution; price / stock / rating slots show
/// skeletons until their sub-calls return.
class ProductDetailV2Screen extends StatelessWidget {
  const ProductDetailV2Screen({
    super.key,
    required this.locale,
    required this.onAddToCart,
    required this.onRequestVerification,
  });

  final String locale;
  final ValueChanged<CatalogProductDetail> onAddToCart;
  final ValueChanged<CatalogProductDetail> onRequestVerification;

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<ProductDetailV2Bloc, ProductDetailV2State>(
      builder: (context, state) {
        if (state.productLoading) {
          return const Scaffold(
            body: Center(child: CircularProgressIndicator()),
          );
        }
        final product = state.product;
        if (product == null) {
          return Scaffold(
            appBar: AppBar(),
            body: _ErrorView(
              failure: state.productFailure,
              onRetry: () => context
                  .read<ProductDetailV2Bloc>()
                  .add(const ProductDetailV2Requested()),
            ),
          );
        }
        return Scaffold(
          appBar: AppBar(title: Text(product.name.resolve(locale))),
          body: RefreshIndicator(
            onRefresh: () async => context
                .read<ProductDetailV2Bloc>()
                .add(const ProductDetailV2Requested()),
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                _Media(urls: product.mediaUrls),
                const SizedBox(height: AppSpacing.md),
                Text(
                  product.name.resolve(locale),
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                if (product.brandName != null) ...[
                  const SizedBox(height: AppSpacing.xs),
                  Text(
                    product.brandName!.resolve(locale),
                    style: TextStyle(color: Theme.of(context).hintColor),
                  ),
                ],
                const SizedBox(height: AppSpacing.md),
                _PriceRow(state: state, locale: locale),
                const SizedBox(height: AppSpacing.sm),
                Row(
                  children: [
                    if (state.availabilityLoading)
                      const _SkeletonChip(width: 70)
                    else
                      StockBadge(
                        availability: state.availability,
                        showInStock: true,
                      ),
                    const Spacer(),
                    if (state.aggregateLoading)
                      const _SkeletonChip(width: 60)
                    else if (state.aggregate != null)
                      RatingBlock(aggregate: state.aggregate),
                  ],
                ),
                const SizedBox(height: AppSpacing.lg),
                _Description(product: product, locale: locale),
                if (product.attributes.isNotEmpty) ...[
                  const SizedBox(height: AppSpacing.lg),
                  _AttributesTable(product: product, locale: locale),
                ],
                const SizedBox(height: AppSpacing.lg),
                RestrictionGate(
                  isRestricted: product.isRestricted,
                  onRequestVerification: () => onRequestVerification(product),
                  child: SizedBox(
                    height: 48,
                    child: FilledButton(
                      onPressed: state.availability?.inStock == false
                          ? null
                          : () => onAddToCart(product),
                      child: const Text('Add to cart'),
                    ),
                  ),
                ),
                if (product.restrictedRationale != null) ...[
                  const SizedBox(height: AppSpacing.sm),
                  Text(
                    product.restrictedRationale!.resolve(locale),
                    style: TextStyle(
                      color: Theme.of(context).hintColor,
                      fontSize: 12,
                    ),
                  ),
                ],
              ],
            ),
          ),
        );
      },
    );
  }
}

class _Media extends StatelessWidget {
  const _Media({required this.urls});
  final List<String> urls;

  @override
  Widget build(BuildContext context) {
    if (urls.isEmpty) {
      return const AspectRatio(
        aspectRatio: 1,
        child: ColoredBox(
          color: AppColors.neutral,
          child: Icon(Icons.image_outlined,
              color: AppColors.textSecondary),
        ),
      );
    }
    return AspectRatio(
      aspectRatio: 1,
      child: PageView.builder(
        itemCount: urls.length,
        itemBuilder: (_, i) => Image.network(
          urls[i],
          fit: BoxFit.cover,
          errorBuilder: (_, __, ___) => const Icon(
            Icons.broken_image_outlined,
            color: AppColors.textSecondary,
          ),
        ),
      ),
    );
  }
}

class _PriceRow extends StatelessWidget {
  const _PriceRow({required this.state, required this.locale});
  final ProductDetailV2State state;
  final String locale;

  @override
  Widget build(BuildContext context) {
    if (state.priceLoading && state.priceQuote == null) {
      return Row(
        children: [
          PriceLabel(money: state.product!.priceHint),
          const SizedBox(width: AppSpacing.sm),
          const _SkeletonChip(width: 80),
        ],
      );
    }
    final price = state.displayPrice;
    if (price == null) return const SizedBox.shrink();
    return Row(
      children: [
        PriceLabel(
          money: price,
          style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700),
        ),
        if (state.priceDrift) ...[
          const SizedBox(width: AppSpacing.sm),
          _DriftBadge(),
        ],
        if (state.priceQuote?.lines.firstOrNull?.tierLabel == 'business') ...[
          const SizedBox(width: AppSpacing.sm),
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: 2,
            ),
            decoration: BoxDecoration(
              color: AppColors.secondary.withValues(alpha: 0.18),
              borderRadius: BorderRadius.circular(4),
            ),
            child: const Text(
              'Business price',
              style: TextStyle(
                color: AppColors.primary,
                fontSize: 11,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ],
    );
  }
}

class _DriftBadge extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: 2,
      ),
      decoration: BoxDecoration(
        color: AppColors.warning.withValues(alpha: 0.18),
        borderRadius: BorderRadius.circular(4),
      ),
      child: const Text(
        'Updated just now',
        style: TextStyle(
          color: AppColors.warning,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _Description extends StatelessWidget {
  const _Description({required this.product, required this.locale});
  final CatalogProductDetail product;
  final String locale;

  @override
  Widget build(BuildContext context) {
    final text = product.description.resolve(locale);
    if (text.isEmpty) return const SizedBox.shrink();
    return Text(text);
  }
}

class _AttributesTable extends StatelessWidget {
  const _AttributesTable({required this.product, required this.locale});
  final CatalogProductDetail product;
  final String locale;

  @override
  Widget build(BuildContext context) {
    return Table(
      columnWidths: const {
        0: IntrinsicColumnWidth(),
        1: FlexColumnWidth(),
      },
      children: [
        for (final entry in product.attributes.entries)
          TableRow(
            decoration: const BoxDecoration(
              border: Border(
                bottom: BorderSide(color: AppColors.neutral),
              ),
            ),
            children: [
              Padding(
                padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
                child: Text(
                  entry.key,
                  style: TextStyle(color: Theme.of(context).hintColor),
                ),
              ),
              Padding(
                padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
                child: Text(entry.value.resolve(locale)),
              ),
            ],
          ),
      ],
    );
  }
}

class _SkeletonChip extends StatelessWidget {
  const _SkeletonChip({required this.width});
  final double width;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: 16,
      decoration: BoxDecoration(
        color: AppColors.neutral,
        borderRadius: BorderRadius.circular(4),
      ),
    );
  }
}

class _ErrorView extends StatelessWidget {
  const _ErrorView({this.failure, required this.onRetry});
  final Object? failure;
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
            const Text('Failed to load product'),
            const SizedBox(height: AppSpacing.sm),
            FilledButton(onPressed: onRetry, child: const Text('Retry')),
          ],
        ),
      ),
    );
  }
}
