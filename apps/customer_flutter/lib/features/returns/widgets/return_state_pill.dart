import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';

/// Single-state pill for a return (S-6.1 list + S-6.3 detail).
///
/// Color mapping follows the Phase 5 state-pill semantics — neutral for
/// pre-decision states, success-green for terminal positive states,
/// danger-red for `rejected`. Unknown wire values fall back to the raw
/// string with the neutral chip color (forward-compat).
class ReturnStatePill extends StatelessWidget {
  const ReturnStatePill({super.key, required this.state});

  /// Wire state — see [kKnownReturnStates] in return_models.dart.
  final String state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final (label, color) = switch (state) {
      'pending' => (l10n.returnStatePending, AppColors.warning),
      'approved' => (l10n.returnStateApproved, AppColors.secondary),
      'received' => (l10n.returnStateReceived, AppColors.secondary),
      'inspected' => (l10n.returnStateInspected, AppColors.secondary),
      'issued' => (l10n.returnStateIssued, AppColors.success),
      'rejected' => (l10n.returnStateRejected, AppColors.danger),
      // CodeRabbit feedback: never surface raw wire enum codes to
      // users. Show a localized "status updating" placeholder when
      // the server sends a forward-compat value the client doesn't
      // know yet.
      _ => (l10n.returnStateUnknown, AppColors.textSecondary),
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
