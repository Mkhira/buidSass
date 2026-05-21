import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/renew_bloc.dart';

/// S-7.4 renew. Pre-fills with prior case details for confirmation
/// then issues a single POST /verifications/renew with
/// Idempotency-Key.
class RenewScreen extends StatelessWidget {
  const RenewScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.verificationRenewTitle)),
      body: BlocConsumer<RenewBloc, RenewState>(
        listener: (context, state) {
          if (state is RenewDone) {
            context.go('/verification/${state.result.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            RenewLoading() => LoadingState(semanticsLabel: l10n.commonLoading),
            RenewLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () {
                  final bloc = context.read<RenewBloc>();
                  final args = bloc.lastStartArgs;
                  if (args == null) {
                    Navigator.of(context).maybePop();
                    return;
                  }
                  bloc.add(RenewStarted(
                    priorVerificationId: args.priorVerificationId,
                    marketCode: args.marketCode,
                  ));
                },
              ),
            RenewReady() => _Ready(state: state, submitting: false),
            RenewSubmitting(:final ready) =>
              _Ready(state: ready, submitting: true),
            RenewDone() => LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Ready extends StatelessWidget {
  const _Ready({required this.state, required this.submitting});
  final RenewReady state;
  final bool submitting;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return SafeArea(
      child: Column(
        children: [
          if (state.formError != null)
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: AppColors.danger.withValues(alpha: 0.1),
                  border: Border.all(color: AppColors.danger),
                  borderRadius: BorderRadius.circular(8),
                ),
                // Always show a stable, localized message — never the
                // raw exception text. The error code is logged via the
                // bloc state for debugging.
                child: Text(
                  l10n.commonErrorBody,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Text(
                  l10n.verificationRenewBody,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                const SizedBox(height: AppSpacing.md),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          state.prior.kind,
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        const SizedBox(height: AppSpacing.sm),
                        for (final entry in state.prior.fields.entries)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 4),
                            child: Text(
                              '${entry.key}: ${entry.value}',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.verificationRenewCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting
                  ? null
                  : () => context.read<RenewBloc>().add(const RenewSubmitted()),
            ),
          ),
        ],
      ),
    );
  }
}
