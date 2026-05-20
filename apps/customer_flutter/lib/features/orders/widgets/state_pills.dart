import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

import '../data/models/order_models.dart';

/// Four-pill row per Phase 5 BR-1 — `orderState`, `paymentState`,
/// `fulfillmentState`, `refundState` rendered independently. ALWAYS
/// renders 4 pills regardless of state values (the spec test for this
/// widget asserts that even an empty bundle produces 4 pills).
class StatePillsRow extends StatelessWidget {
  const StatePillsRow({super.key, required this.states});

  final OrderStateBundle states;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: [
        _Pill(label: 'Order', value: states.orderState, palette: _orderPalette),
        _Pill(
            label: 'Payment',
            value: states.paymentState,
            palette: _paymentPalette),
        _Pill(
            label: 'Fulfillment',
            value: states.fulfillmentState,
            palette: _fulfillmentPalette),
        _Pill(
            label: 'Refund',
            value: states.refundState,
            palette: _refundPalette),
      ],
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({
    required this.label,
    required this.value,
    required this.palette,
  });

  final String label;
  final String value;
  final Map<String, Color> palette;

  @override
  Widget build(BuildContext context) {
    final color = palette[value] ?? Theme.of(context).colorScheme.outline;
    return DecoratedBox(
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm, vertical: AppSpacing.xs),
        child: Text(
          '$label · $value',
          style: Theme.of(context)
              .textTheme
              .labelSmall
              ?.copyWith(color: color, fontWeight: FontWeight.w600),
        ),
      ),
    );
  }
}

const _orderPalette = <String, Color>{
  'placed': Colors.blue,
  'confirmed': Colors.teal,
  'completed': Colors.green,
  'cancelled': Colors.red,
};

const _paymentPalette = <String, Color>{
  'pending': Colors.orange,
  'captured': Colors.green,
  'failed': Colors.red,
  'refunded': Colors.purple,
};

const _fulfillmentPalette = <String, Color>{
  'pending': Colors.grey,
  'picking': Colors.blue,
  'packed': Colors.indigo,
  'shipped': Colors.teal,
  'delivered': Colors.green,
};

const _refundPalette = <String, Color>{
  'none': Colors.grey,
  'requested': Colors.orange,
  'approved': Colors.blue,
  'issued': Colors.green,
  'rejected': Colors.red,
};
