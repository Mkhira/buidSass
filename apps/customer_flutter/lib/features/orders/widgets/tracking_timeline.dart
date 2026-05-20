import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/models/order_models.dart';

/// Vertical timeline rendering `OrderShipment.events[]` (S-5.5). Top
/// event is the live one (highlighted); prior events are dimmed.
/// AR layout mirrors automatically via Flutter's Directionality.
class TrackingTimeline extends StatelessWidget {
  const TrackingTimeline({super.key, required this.events});
  final List<OrderTrackingEvent> events;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    if (events.isEmpty) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Text(
          l10n.orderTrackingEmpty,
          style: Theme.of(context).textTheme.bodyMedium,
        ),
      );
    }
    // Server returns chronological; render most recent first.
    final sorted = [...events]
      ..sort((a, b) => b.occurredAt.compareTo(a.occurredAt));
    final locale = Localizations.localeOf(context).toString();
    final fmt = DateFormat.yMMMd(locale).add_jm();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (var i = 0; i < sorted.length; i++)
          _TimelineRow(
            event: sorted[i],
            isLive: i == 0,
            isLast: i == sorted.length - 1,
            timeFormatter: fmt,
          ),
      ],
    );
  }
}

class _TimelineRow extends StatelessWidget {
  const _TimelineRow({
    required this.event,
    required this.isLive,
    required this.isLast,
    required this.timeFormatter,
  });
  final OrderTrackingEvent event;
  final bool isLive;
  final bool isLast;
  final DateFormat timeFormatter;

  @override
  Widget build(BuildContext context) {
    final color = isLive
        ? Theme.of(context).colorScheme.primary
        : Theme.of(context).colorScheme.outline;
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Column(
            children: [
              Container(
                width: 12,
                height: 12,
                margin: const EdgeInsets.only(top: 4),
                decoration: BoxDecoration(
                  color: color,
                  shape: BoxShape.circle,
                ),
              ),
              if (!isLast)
                Expanded(
                  child:
                      Container(width: 2, color: color.withValues(alpha: 0.5)),
                ),
            ],
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    event.label,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: color,
                          fontWeight:
                              isLive ? FontWeight.w600 : FontWeight.normal,
                        ),
                  ),
                  Text(
                    timeFormatter.format(event.occurredAt),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
