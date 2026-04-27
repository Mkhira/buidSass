import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../data/order_view_models.dart';

class OrderTimeline extends StatelessWidget {
  const OrderTimeline({super.key, required this.events, this.localeCode});
  final List<TimelineEvent> events;
  final String? localeCode;

  @override
  Widget build(BuildContext context) {
    if (events.isEmpty) return const SizedBox.shrink();
    final fmt = DateFormat.yMd(localeCode ?? 'en').add_Hm();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: events.map((e) {
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 8,
                height: 8,
                margin: const EdgeInsets.only(top: 6, right: AppSpacing.sm),
                decoration: const BoxDecoration(
                  color: AppColors.primary,
                  shape: BoxShape.circle,
                ),
              ),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${e.stream} · ${e.fromState} → ${e.toState}',
                        style: AppTypography.body),
                    Text(fmt.format(e.at),
                        style: AppTypography.caption),
                    if (e.reasonNote != null && e.reasonNote!.isNotEmpty)
                      Text(e.reasonNote!, style: AppTypography.caption),
                  ],
                ),
              ),
            ],
          ),
        );
      }).toList(growable: false),
    );
  }
}
