import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/config/feature_flags.dart';
import '../../../generated/l10n/app_localizations.dart';

class VerificationCtaScreen extends StatelessWidget {
  const VerificationCtaScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final flags = context.read<FeatureFlags>();
    return Scaffold(
      appBar: AppBar(title: Text(l10n.moreVerification)),
      body: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(l10n.verificationRequired, style: AppTypography.headline),
            const SizedBox(height: AppSpacing.md),
            Text(
              flags.verificationCtaShipped ? l10n.commonContinue : l10n.commonEmpty,
              style: AppTypography.body,
            ),
            const SizedBox(height: AppSpacing.lg),
            AppButton(
              label: l10n.commonContinue,
              expand: true,
              onPressed: flags.verificationCtaShipped ? () {} : null,
            ),
          ],
        ),
      ),
    );
  }
}
