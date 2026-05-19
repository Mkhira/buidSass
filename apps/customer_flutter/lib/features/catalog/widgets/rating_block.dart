import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

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
    this.labels = const RatingBlockLabels(),
    this.locale,
  });

  final ReviewsAggregate? aggregate;
  final bool showHistogram;
  final bool compact;

  /// Caller-supplied locale-resolved copy. Defaults are English
  /// placeholders so tests + initial wiring compile; the real strings
  /// are passed in by the screen layer once the Phase 6 i18n catalog
  /// lands.
  final RatingBlockLabels labels;

  /// Override locale for digit shaping. When null, resolved from the
  /// surrounding `Localizations` so AR renders Arabic-Indic digits.
  final String? locale;

  @override
  Widget build(BuildContext context) {
    final a = aggregate;
    if (a == null || a.ratingCount == 0) return const SizedBox.shrink();
    final resolvedLocale =
        locale ?? Localizations.localeOf(context).toLanguageTag();
    final avg = NumberFormat.decimalPatternDigits(
      locale: resolvedLocale,
      decimalDigits: 1,
    ).format(a.ratingAverage);
    final compact1k = NumberFormat.compact(locale: resolvedLocale);
    final countLabel = a.ratingCount >= 1000
        ? compact1k.format(a.ratingCount)
        : NumberFormat.decimalPattern(resolvedLocale).format(a.ratingCount);
    final summary = Semantics(
      label: labels.semanticLabel(avg, a.ratingCount),
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
        _StarHistogram(
          distribution: a.starHistogram,
          locale: resolvedLocale,
        ),
      ],
    );
  }
}

class _StarHistogram extends StatelessWidget {
  const _StarHistogram({required this.distribution, required this.locale});
  final List<int> distribution;
  final String locale;

  @override
  Widget build(BuildContext context) {
    final total = distribution.fold<int>(0, (a, b) => a + b);
    if (total == 0) return const SizedBox.shrink();
    final number = NumberFormat.decimalPattern(locale);
    return Column(
      children: List.generate(5, (idx) {
        final star = 5 - idx;
        final count =
            star - 1 < distribution.length ? distribution[star - 1] : 0;
        final ratio = total == 0 ? 0.0 : count / total;
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 2),
          child: Row(
            children: [
              SizedBox(
                width: 14,
                child: Text(
                  number.format(star),
                  style: const TextStyle(fontSize: 12),
                ),
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
                  number.format(count),
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

/// Locale-resolved copy for [RatingBlock]. Defaults are English
/// placeholders for tests + initial wiring; the surrounding screen layer
/// is expected to pass locale-resolved copy (e.g. from AppLocalizations)
/// once the Phase 6 i18n catalog ships.
class RatingBlockLabels {
  const RatingBlockLabels({this.semanticLabelBuilder});

  /// Customizable builder for the accessibility label. Receives the
  /// locale-formatted average string + raw review count; returns the
  /// announced phrase. Default is English.
  final String Function(String avg, int count)? semanticLabelBuilder;

  String semanticLabel(String avg, int count) => semanticLabelBuilder != null
      ? semanticLabelBuilder!(avg, count)
      : 'Rating $avg from $count reviews';
}
