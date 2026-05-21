import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// Single-state pill for a verification case (S-7.1 list banner + S-7.3
/// detail). Mirrors `ReturnStatePill` semantics — neutral pre-decision,
/// success-green for `approved`, danger-red for `rejected`, warning for
/// `info_requested`, neutral-grey for `expired`. Unknown wire values
/// fall back to a localized "status updating" placeholder so we never
/// leak raw enum codes to users.
class VerificationStatePill extends StatelessWidget {
  const VerificationStatePill({super.key, required this.state});

  final String state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final (label, color) = switch (state) {
      'submitted' => (l10n.verificationStateSubmitted, AppColors.secondary),
      'info_requested' => (
          l10n.verificationStateInfoRequested,
          AppColors.warning
        ),
      'approved' => (l10n.verificationStateApproved, AppColors.success),
      'rejected' => (l10n.verificationStateRejected, AppColors.danger),
      'expired' => (l10n.verificationStateExpired, AppColors.textSecondary),
      _ => (l10n.verificationStateUnknown, AppColors.textSecondary),
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
