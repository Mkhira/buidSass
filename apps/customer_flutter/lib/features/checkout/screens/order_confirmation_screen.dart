import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';

class OrderConfirmationScreen extends StatelessWidget {
  const OrderConfirmationScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(),
      body: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.check_circle, size: 80, color: AppColors.success),
            const SizedBox(height: AppSpacing.md),
            Text(l10n.commonOk, style: AppTypography.headline),
            const SizedBox(height: AppSpacing.sm),
            Text(orderId, style: AppTypography.body),
            const SizedBox(height: AppSpacing.lg),
            AppButton(
              label: l10n.navOrders,
              expand: true,
              onPressed: () => context.go('/o/$orderId'),
            ),
          ],
        ),
      ),
    );
  }
}
