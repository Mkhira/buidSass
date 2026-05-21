import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/invitation_accept_bloc.dart';

class InvitationAcceptScreen extends StatelessWidget {
  const InvitationAcceptScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.invitationsTitle)),
      body: BlocConsumer<InvitationAcceptBloc, InvitationAcceptState>(
        listener: (context, state) {
          if (state is InvitationAcceptAccepted) {
            context.go('/company/${state.result.companyId}');
          }
          if (state is InvitationAcceptDeclined) {
            context.go('/');
          }
        },
        builder: (context, state) {
          return switch (state) {
            InvitationAcceptValidating() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            InvitationAcceptExpired() => EmptyState(
                title: l10n.invitationsExpiredTitle,
                body: l10n.invitationsExpiredBody,
                icon: Icons.hourglass_disabled_outlined,
              ),
            InvitationAcceptFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
              ),
            InvitationAcceptReady() => _Ready(state: state),
            InvitationAcceptAccepted() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            InvitationAcceptDeclined() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Ready extends StatelessWidget {
  const _Ready({required this.state});
  final InvitationAcceptReady state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (state.formError != null)
              Container(
                margin: const EdgeInsets.only(bottom: AppSpacing.md),
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: AppColors.danger.withValues(alpha: 0.1),
                  border: Border.all(color: AppColors.danger),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  l10n.commonErrorBody,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            Expanded(
              child: Center(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: Text(
                    l10n.invitationsTitle,
                    style: Theme.of(context).textTheme.titleMedium,
                    textAlign: TextAlign.center,
                  ),
                ),
              ),
            ),
            AppButton(
              label: l10n.invitationsAcceptCta,
              expand: true,
              isLoading: state.submitting,
              onPressed: state.submitting
                  ? null
                  : () => context
                      .read<InvitationAcceptBloc>()
                      .add(const InvitationAccepted()),
            ),
            const SizedBox(height: AppSpacing.sm),
            OutlinedButton(
              onPressed: state.submitting
                  ? null
                  : () => context
                      .read<InvitationAcceptBloc>()
                      .add(const InvitationDeclined()),
              child: Text(l10n.invitationsDeclineCta),
            ),
          ],
        ),
      ),
    );
  }
}
