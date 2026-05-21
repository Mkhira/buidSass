import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// 1–5 star rating widget. Read-only when [onChanged] is null. Uses a
/// single Semantics adjustable node so screen readers expose the slider
/// behavior properly.
class StarsInput extends StatelessWidget {
  const StarsInput({
    super.key,
    required this.value,
    this.onChanged,
    this.size = 32,
  });

  final int value;
  final ValueChanged<int>? onChanged;
  final double size;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Semantics(
      slider: true,
      value: l10n.reviewRatingValue(value),
      increasedValue: value < 5 ? l10n.reviewRatingValue(value + 1) : null,
      decreasedValue: value > 1 ? l10n.reviewRatingValue(value - 1) : null,
      onIncrease:
          onChanged == null || value >= 5 ? null : () => onChanged!(value + 1),
      onDecrease:
          onChanged == null || value <= 1 ? null : () => onChanged!(value - 1),
      child: ExcludeSemantics(
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (var i = 1; i <= 5; i++)
              GestureDetector(
                onTap: onChanged == null ? null : () => onChanged!(i),
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 2),
                  child: Icon(
                    i <= value ? Icons.star : Icons.star_border,
                    size: size,
                    color: i <= value
                        ? AppColors.warning
                        : AppColors.textSecondary,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
