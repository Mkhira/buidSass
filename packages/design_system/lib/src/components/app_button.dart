import 'package:flutter/material.dart';

import '../tokens/app_colors.dart';
import '../tokens/app_spacing.dart';

enum AppButtonVariant { primary, secondary, danger, ghost }

class AppButton extends StatelessWidget {
  const AppButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = AppButtonVariant.primary,
    this.icon,
    this.isLoading = false,
    this.expand = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final AppButtonVariant variant;
  final IconData? icon;
  final bool isLoading;
  final bool expand;

  @override
  Widget build(BuildContext context) {
    final disabled = onPressed == null || isLoading;
    final colors = _colorsFor(variant);
    final child = isLoading
        ? const SizedBox(
            height: 18,
            width: 18,
            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
          )
        : Row(
            mainAxisSize: expand ? MainAxisSize.max : MainAxisSize.min,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (icon != null) ...[
                Icon(icon, size: 18, color: colors.fg),
                const SizedBox(width: AppSpacing.sm),
              ],
              Text(
                label,
                style: TextStyle(color: colors.fg, fontWeight: FontWeight.w600),
              ),
            ],
          );

    final button = ElevatedButton(
      onPressed: disabled ? null : onPressed,
      style: ElevatedButton.styleFrom(
        backgroundColor: colors.bg,
        foregroundColor: colors.fg,
        disabledBackgroundColor: colors.bg.withValues(alpha: 0.4),
        disabledForegroundColor: colors.fg.withValues(alpha: 0.7),
        elevation: 0,
        padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.lg,
          vertical: AppSpacing.md,
        ),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      child: child,
    );

    return expand ? SizedBox(width: double.infinity, child: button) : button;
  }

  ({Color bg, Color fg}) _colorsFor(AppButtonVariant v) {
    switch (v) {
      case AppButtonVariant.primary:
        return (bg: AppColors.primary, fg: Colors.white);
      case AppButtonVariant.secondary:
        return (bg: AppColors.secondary, fg: Colors.white);
      case AppButtonVariant.danger:
        return (bg: AppColors.danger, fg: Colors.white);
      case AppButtonVariant.ghost:
        return (bg: Colors.transparent, fg: AppColors.primary);
    }
  }
}
