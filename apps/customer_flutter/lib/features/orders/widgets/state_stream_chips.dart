import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

/// Per Principle 17 + FR-025 — order, payment, fulfillment, refund states
/// MUST render as four independent signals, not one collapsed status.
class StateStreamChips extends StatelessWidget {
  const StateStreamChips({
    super.key,
    required this.orderState,
    required this.paymentState,
    required this.fulfillmentState,
    required this.refundState,
  });

  final String orderState;
  final String paymentState;
  final String fulfillmentState;
  final String refundState;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: AppSpacing.sm,
      runSpacing: AppSpacing.xs,
      children: [
        _Chip(label: 'order', value: orderState, color: AppColors.primary),
        _Chip(
            label: 'payment', value: paymentState, color: AppColors.secondary),
        _Chip(
          label: 'fulfillment',
          value: fulfillmentState,
          color: AppColors.accent,
        ),
        _Chip(label: 'refund', value: refundState, color: AppColors.warning),
      ],
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.value, required this.color});
  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: '$label: $value',
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm,
          vertical: AppSpacing.xs,
        ),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.1),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: color),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(label,
                style: AppTypography.caption
                    .copyWith(color: color, fontWeight: FontWeight.w600)),
            const SizedBox(width: AppSpacing.xs),
            Text(value,
                style: AppTypography.caption
                    .copyWith(color: AppColors.textPrimary)),
          ],
        ),
      ),
    );
  }
}
