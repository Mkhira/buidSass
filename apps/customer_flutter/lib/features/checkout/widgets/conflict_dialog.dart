import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_drift.dart';

/// Modal shown on 409 drift (BR-4 / S-4.9). Returns the user's chosen
/// [DriftResolution] so the calling bloc can branch on accept (run
/// `accept-drift` then re-run the original PATCH/POST) vs review (route
/// to summary).
Future<DriftResolution?> showConflictDialog(
  BuildContext context,
  CheckoutConflict conflict,
) {
  return showDialog<DriftResolution>(
    context: context,
    barrierDismissible: false,
    builder: (ctx) => _ConflictDialog(conflict: conflict),
  );
}

class _ConflictDialog extends StatelessWidget {
  const _ConflictDialog({required this.conflict});
  final CheckoutConflict conflict;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final deltas = conflict.details.deltas;
    return AlertDialog(
      title: Text(l10n.checkoutDriftTitle),
      content: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(l10n.checkoutDriftBody),
            if (deltas.isNotEmpty) ...[
              const SizedBox(height: AppSpacing.md),
              for (final d in deltas)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
                  child: Text(
                    '${d.kind} · ${d.productId}: ${d.before ?? '?'} → ${d.after ?? '?'}',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(DriftResolution.review),
          child: Text(l10n.checkoutDriftReview),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(DriftResolution.accept),
          child: Text(l10n.checkoutDriftAccept),
        ),
      ],
    );
  }
}
