import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../data/models/quote_models.dart';

/// Wire enum of triggerable quote actions. Kept as a sealed enum so
/// the bloc can dispatch a single `QuoteActionRequested(kind)` event
/// rather than having one event per button.
enum QuoteActionKind {
  submitAcceptance,
  finalizeAcceptance,
  rejectAcceptance,
  requestRevision,
  withdraw,
  saveAsTemplate,
}

/// Toolbar that mirrors `actions.*` from the detail payload. Buttons
/// are gated on the server's allowlist (BR-2). The acceptance step
/// badge (1 of 2 / 2 of 2) is rendered above the row so the user
/// always sees the current phase of the two-step acceptance.
class QuoteActionsToolbar extends StatelessWidget {
  const QuoteActionsToolbar({
    super.key,
    required this.actions,
    required this.busyAction,
    required this.onAction,
  });

  final QuoteActions actions;

  /// The action currently being submitted (so its button shows a
  /// spinner). All other actions disable while one is in-flight.
  final QuoteActionKind? busyAction;
  final ValueChanged<QuoteActionKind> onAction;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final stepBadge = actions.canFinalizeAcceptance
        ? l10n.quoteAcceptanceStep2of2
        : actions.canSubmitAcceptance
            ? l10n.quoteAcceptanceStep1of2
            : null;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (stepBadge != null) ...[
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: AppSpacing.xs,
            ),
            decoration: BoxDecoration(
              color: AppColors.warning.withValues(alpha: 0.15),
              border: Border.all(color: AppColors.warning),
              borderRadius: BorderRadius.circular(999),
            ),
            child: Text(
              stepBadge,
              style: const TextStyle(
                color: AppColors.warning,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
        ],
        Wrap(
          spacing: AppSpacing.sm,
          runSpacing: AppSpacing.sm,
          children: [
            if (actions.canSubmitAcceptance)
              _btn(
                context,
                label: l10n.quoteActionSubmitAcceptance,
                kind: QuoteActionKind.submitAcceptance,
                primary: true,
              ),
            if (actions.canFinalizeAcceptance)
              _btn(
                context,
                label: l10n.quoteActionFinalizeAcceptance,
                kind: QuoteActionKind.finalizeAcceptance,
                primary: true,
              ),
            if (actions.canRejectAcceptance)
              _btn(
                context,
                label: l10n.quoteActionRejectAcceptance,
                kind: QuoteActionKind.rejectAcceptance,
                danger: true,
              ),
            if (actions.canRequestRevision)
              _btn(
                context,
                label: l10n.quoteActionRequestRevision,
                kind: QuoteActionKind.requestRevision,
              ),
            if (actions.canWithdraw)
              _btn(
                context,
                label: l10n.quoteActionWithdraw,
                kind: QuoteActionKind.withdraw,
              ),
            if (actions.canSaveAsTemplate)
              _btn(
                context,
                label: l10n.quoteActionSaveAsTemplate,
                kind: QuoteActionKind.saveAsTemplate,
              ),
          ],
        ),
      ],
    );
  }

  Widget _btn(
    BuildContext context, {
    required String label,
    required QuoteActionKind kind,
    bool primary = false,
    bool danger = false,
  }) {
    final isBusy = busyAction == kind;
    final disabled = busyAction != null && !isBusy;
    if (primary) {
      return AppButton(
        label: label,
        isLoading: isBusy,
        onPressed: disabled ? null : () => onAction(kind),
      );
    }
    return OutlinedButton(
      onPressed: disabled
          ? null
          : isBusy
              ? null
              : () => onAction(kind),
      style: OutlinedButton.styleFrom(
        foregroundColor: danger ? AppColors.danger : null,
        side: BorderSide(color: danger ? AppColors.danger : AppColors.primary),
      ),
      child: isBusy
          ? const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : Text(label),
    );
  }
}
