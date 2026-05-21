import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/memberships_bloc.dart';
import '../widgets/member_row.dart';

class MembershipsScreen extends StatelessWidget {
  const MembershipsScreen({super.key, required this.companyId});
  final String companyId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.membershipsTitle)),
      body: BlocBuilder<MembershipsBloc, MembershipsState>(
        builder: (context, state) {
          return switch (state) {
            MembershipsLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            MembershipsLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<MembershipsBloc>()
                    .add(const MembershipsStarted()),
              ),
            MembershipsLoaded() => _Loaded(state: state),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final MembershipsLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final canManage = state.company.isAdmin;
    return RefreshIndicator(
      onRefresh: () async {
        final bloc = context.read<MembershipsBloc>();
        bloc.add(const MembershipsRefreshed());
        await bloc.stream.firstWhere((s) => s is MembershipsLoaded).timeout(
              const Duration(seconds: 10),
              onTimeout: () => bloc.state,
            );
      },
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          if (state.actionError != null)
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
          for (final m in state.company.memberships)
            MemberRow(
              member: m,
              canManage: canManage,
              busy: state.busyMembershipId == m.id,
              onRoleChanged: !canManage
                  ? null
                  : (role) => context
                      .read<MembershipsBloc>()
                      .add(MembershipsRoleChanged(
                        membershipId: m.id,
                        role: role,
                      )),
              onRemoveRequested: !canManage
                  ? null
                  : () async {
                      final confirm = await _confirmRemove(context, m.name);
                      if (!confirm) return;
                      if (!context.mounted) return;
                      context
                          .read<MembershipsBloc>()
                          .add(MembershipsRemoveRequested(m.id));
                    },
            ),
        ],
      ),
    );
  }

  Future<bool> _confirmRemove(BuildContext context, String name) async {
    final l10n = AppLocalizations.of(context);
    final result = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(l10n.membershipsRemoveConfirmTitle),
        content: Text(l10n.membershipsRemoveConfirmBody(name)),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: Text(l10n.commonCancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: Text(l10n.membershipsRemoveCta),
          ),
        ],
      ),
    );
    return result == true;
  }
}
