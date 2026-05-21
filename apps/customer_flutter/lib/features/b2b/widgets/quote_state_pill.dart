import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// State pill for the b2b quote 8-value lifecycle. Mirrors the
/// verification/review/return pill semantics — neutral for pre-decision
/// states, success-green for accepted, danger-red for rejected, neutral
/// grey for terminal-negative (withdrawn / expired). Unknown wire
/// values fall back to a localized "status updating" placeholder so we
/// never leak raw enum codes to users.
class QuoteStatePill extends StatelessWidget {
  const QuoteStatePill({super.key, required this.state});
  final String state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final (label, color) = switch (state) {
      'draft' => (l10n.quoteStateDraft, AppColors.textSecondary),
      'published' => (l10n.quoteStatePublished, AppColors.secondary),
      'awaiting_acceptance' => (
          l10n.quoteStateAwaitingAcceptance,
          AppColors.warning
        ),
      'awaiting_finalization' => (
          l10n.quoteStateAwaitingFinalization,
          AppColors.warning
        ),
      'accepted' => (l10n.quoteStateAccepted, AppColors.success),
      'rejected' => (l10n.quoteStateRejected, AppColors.danger),
      'withdrawn' => (l10n.quoteStateWithdrawn, AppColors.textSecondary),
      'expired' => (l10n.quoteStateExpired, AppColors.textSecondary),
      _ => (l10n.quoteStateUnknown, AppColors.textSecondary),
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
