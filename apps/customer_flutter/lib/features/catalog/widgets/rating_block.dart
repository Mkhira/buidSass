import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

import '../../reviews/data/models/reviews_aggregate_models.dart';

/// S-2.7 — read-only rating block.
///
/// On list cards: compact horizontal layout (★ 4.7 · 125).
/// On PDP: same row with optional star histogram below.
class RatingBlock extends StatelessWidget {
  const RatingBlock({
    super.key,
    required this.aggregate,
    this.showHistogram = false,
    this.compact = true,
  });

  final ReviewsAggregate? aggregate;
  final bool showHistogram;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final a = aggregate;
    if (a == null || a.ratingCount == 0) return const SizedBox.shrink();
    final avg = a.ratingAverage.toStringAsFixed(1);
    final countLabel = a.ratingCount >= 1000
        ? '${(a.ratingCount / 1000).toStringAsFixed(1)}k'
        : a.ratingCount.toString();
    final summary = Semantics(
      label: 'Rating $avg from ${a.ratingCount} reviews',
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.star_rounded, size: 16, color: AppColors.warning),
          const SizedBox(width: AppSpacing.xs),
          Text(
            avg,
            style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
          ),
          const SizedBox(width: AppSpacing.xs),
          Text(
            '($countLabel)',
            style: TextStyle(
              fontSize: 12,
              color: Theme.of(context).hintColor,
            ),
          ),
        ],
      ),
    );
    if (!showHistogram || a.starHistogram.isEmpty) return summary;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        summary,
        const SizedBox(height: AppSpacing.sm),
        _StarHistogram(distribution: a.starHistogram),
      ],
    );
  }
}

class _StarHistogram extends StatelessWidget {
  const _StarHistogram({required this.distribution});
  final List<int> distribution;

  @override
  Widget build(BuildContext context) {
    final total = distribution.fold<int>(0, (a, b) => a + b);
    if (total == 0) return const SizedBox.shrink();
    return Column(
      children: List.generate(5, (idx) {
        final star = 5 - idx;
        final count = star - 1 < distribution.length
            ? distribution[star - 1]
            : 0;
        final ratio = total == 0 ? 0.0 : count / total;
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 2),
          child: Row(
            children: [
              SizedBox(
                width: 14,
                child: Text('$star', style: const TextStyle(fontSize: 12)),
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(2),
                  child: LinearProgressIndicator(
                    value: ratio,
                    minHeight: 6,
                    backgroundColor: AppColors.neutral,
                    color: AppColors.primary,
                  ),
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              SizedBox(
                width: 32,
                child: Text(
                  '$count',
                  textAlign: TextAlign.end,
                  style: TextStyle(
                    fontSize: 12,
                    color: Theme.of(context).hintColor,
                  ),
                ),
              ),
            ],
          ),
        );
      }),
    );
  }
}
