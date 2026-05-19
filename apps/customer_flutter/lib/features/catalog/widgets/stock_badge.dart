import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

import '../../inventory/data/models/inventory_models.dart';

/// S-2.8 — three-state stock badge driven by [InventoryAvailability].
/// Out-of-stock is the loudest signal (danger color); low-stock is the
/// nudge (warning); in-stock surfaces only as a subtle confirmation when
/// [showInStock] is true (defaults off so list cards stay calm).
class StockBadge extends StatelessWidget {
  const StockBadge({
    super.key,
    required this.availability,
    this.showInStock = false,
    this.labels = const StockBadgeLabels(),
  });

  final InventoryAvailability? availability;
  final bool showInStock;
  final StockBadgeLabels labels;

  @override
  Widget build(BuildContext context) {
    final av = availability;
    if (av == null) return const SizedBox.shrink();
    final state = av.badgeState;
    if (state == 'inStock' && !showInStock) return const SizedBox.shrink();
    final (label, color) = switch (state) {
      'outOfStock' => (labels.outOfStock, AppColors.danger),
      'low' => (labels.low, AppColors.warning),
      _ => (labels.inStock, AppColors.success),
    };
    return Semantics(
      label: label,
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm,
          vertical: AppSpacing.xs,
        ),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(AppSpacing.xs),
        ),
        child: Text(
          label,
          style: TextStyle(
            color: color,
            fontWeight: FontWeight.w600,
            fontSize: 12,
          ),
        ),
      ),
    );
  }
}

/// Locale-keyed labels. Phase 2 uses plain strings (no l10n keys yet);
/// callers pass locale-resolved copy when wiring screens. Defaults are
/// English placeholders so widget tests can compile without setup.
class StockBadgeLabels {
  const StockBadgeLabels({
    this.inStock = 'In stock',
    this.low = 'Low stock',
    this.outOfStock = 'Out of stock',
  });

  final String inStock;
  final String low;
  final String outOfStock;
}
