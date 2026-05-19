import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';

/// BR-1 / Principle 8 — Restricted products stay visible with prices
/// visible; this widget renders the disabled add-to-cart CTA + the
/// "requires verification" explainer linking to Phase 7's verification
/// flow. Used by product cards (compact) and the PDP CTA (full).
///
/// When [isRestricted] is false the widget renders [child] verbatim, so
/// callers can wrap an add-to-cart button unconditionally.
class RestrictionGate extends StatelessWidget {
  const RestrictionGate({
    super.key,
    required this.isRestricted,
    required this.child,
    required this.onRequestVerification,
    this.copy = const RestrictionGateCopy(),
    this.compact = false,
  });

  final bool isRestricted;
  final Widget child;
  final VoidCallback onRequestVerification;
  final RestrictionGateCopy copy;

  /// Compact layout for list cards: single inline "Verify to buy" pill.
  /// Full layout (PDP): disabled CTA above a one-line explainer + link.
  final bool compact;

  @override
  Widget build(BuildContext context) {
    if (!isRestricted) return child;
    if (compact) {
      return Semantics(
        label: copy.compactLabel,
        button: true,
        child: InkWell(
          onTap: onRequestVerification,
          borderRadius: BorderRadius.circular(AppSpacing.xs),
          child: Container(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: AppSpacing.xs,
            ),
            decoration: BoxDecoration(
              border: Border.all(color: AppColors.warning),
              borderRadius: BorderRadius.circular(AppSpacing.xs),
            ),
            child: Text(
              copy.compactLabel,
              style: const TextStyle(
                color: AppColors.warning,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ),
        ),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AbsorbPointer(
          child: Opacity(opacity: 0.55, child: child),
        ),
        const SizedBox(height: AppSpacing.sm),
        Row(
          children: [
            const Icon(Icons.lock_outline, size: 16, color: AppColors.warning),
            const SizedBox(width: AppSpacing.xs),
            Expanded(
              child: Text(
                copy.explainer,
                style: const TextStyle(fontSize: 12),
              ),
            ),
            TextButton(
              onPressed: onRequestVerification,
              child: Text(copy.cta),
            ),
          ],
        ),
      ],
    );
  }
}

class RestrictionGateCopy {
  const RestrictionGateCopy({
    this.compactLabel = 'Verify to buy',
    this.explainer = 'This product requires professional verification.',
    this.cta = 'Verify',
  });

  final String compactLabel;
  final String explainer;
  final String cta;
}
