import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// State pill for a review row (S-7.6 / S-7.7). Mirrors the
/// return + verification pill semantics — neutral pending, success
/// visible, warning flagged, danger hidden, fallback "status updating"
/// for forward-compat values.
class ReviewStatePill extends StatelessWidget {
  const ReviewStatePill({super.key, required this.state});
  final String state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final (label, color) = switch (state) {
      'pending_moderation' =>
        (l10n.reviewStatePendingModeration, AppColors.warning),
      'visible' => (l10n.reviewStateVisible, AppColors.success),
      'flagged' => (l10n.reviewStateFlagged, AppColors.warning),
      'hidden' => (l10n.reviewStateHidden, AppColors.danger),
      _ => (l10n.reviewStateUnknown, AppColors.textSecondary),
    };
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: color,
          fontWeight: FontWeight.w600,
          fontSize: 12,
        ),
      ),
    );
  }
}
