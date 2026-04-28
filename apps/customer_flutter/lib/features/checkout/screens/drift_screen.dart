import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_bloc.dart';

class DriftScreen extends StatelessWidget {
  const DriftScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(),
      body: BlocBuilder<CheckoutBloc, CheckoutState>(
        builder: (context, state) {
          if (state is! CheckoutDriftBlocked) {
            return EmptyState(title: l10n.commonEmpty);
          }
          return Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              children: [
                Text(l10n.commonErrorTitle, style: AppTypography.headline),
                const SizedBox(height: AppSpacing.md),
                Text('${state.details.changedLines.length} lines updated'),
                const SizedBox(height: AppSpacing.md),
                AppButton(
                  label: l10n.commonContinue,
                  expand: true,
                  onPressed: () =>
                      context.read<CheckoutBloc>().add(const DriftAccepted()),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
