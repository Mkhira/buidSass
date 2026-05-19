import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

import '../../inventory/data/models/inventory_models.dart';
import '../../reviews/data/models/reviews_aggregate_models.dart';
import '../data/models/catalog_models.dart';
import 'price_label.dart';
import 'rating_block.dart';
import 'restriction_gate.dart';
import 'stock_badge.dart';

/// Composable product card used by Home featured strip, category lists,
/// brand lists. Inventory and aggregate slots are nullable so the card
/// can render skeleton-ish ASAP and fill in stock/rating when those calls
/// return (BR-3, BR-4).
class ProductCard extends StatelessWidget {
  const ProductCard({
    super.key,
    required this.product,
    required this.locale,
    required this.onTap,
    required this.onAddToCart,
    required this.onRequestVerification,
    this.availability,
    this.aggregate,
    this.showBrand = true,
  });

  final CatalogProduct product;
  final String locale;
  final InventoryAvailability? availability;
  final ReviewsAggregate? aggregate;
  final VoidCallback onTap;
  final VoidCallback onAddToCart;
  final VoidCallback onRequestVerification;
  final bool showBrand;

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.sm),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              AspectRatio(
                aspectRatio: 1,
                child: ColoredBox(
                  color: AppColors.neutral,
                  child: product.thumbnailUrl.isEmpty
                      ? const Icon(Icons.image_outlined,
                          color: AppColors.textSecondary)
                      : Image.network(
                          product.thumbnailUrl,
                          fit: BoxFit.cover,
                          errorBuilder: (_, __, ___) => const Icon(
                            Icons.broken_image_outlined,
                            color: AppColors.textSecondary,
                          ),
                        ),
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              Text(
                product.name.resolve(locale),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              if (showBrand && product.brandName != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  product.brandName!.resolve(locale),
                  style: TextStyle(
                    fontSize: 12,
                    color: Theme.of(context).hintColor,
                  ),
                ),
              ],
              const SizedBox(height: AppSpacing.sm),
              PriceLabel(money: product.priceHint),
              const SizedBox(height: AppSpacing.xs),
              Row(
                children: [
                  StockBadge(availability: availability),
                  if (aggregate != null) ...[
                    const Spacer(),
                    RatingBlock(aggregate: aggregate),
                  ],
                ],
              ),
              const SizedBox(height: AppSpacing.sm),
              SizedBox(
                width: double.infinity,
                child: RestrictionGate(
                  isRestricted: product.isRestricted,
                  onRequestVerification: onRequestVerification,
                  compact: true,
                  child: FilledButton(
                    onPressed: availability?.inStock == false
                        ? null
                        : onAddToCart,
                    child: const Text('Add to cart'),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
