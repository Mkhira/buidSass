import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/branches_bloc.dart';
import '../data/models/company_models.dart';

class BranchesScreen extends StatelessWidget {
  const BranchesScreen({super.key, required this.companyId});
  final String companyId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.branchesTitle)),
      body: BlocBuilder<BranchesBloc, BranchesState>(
        builder: (context, state) {
          return switch (state) {
            BranchesLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            BranchesLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () =>
                    context.read<BranchesBloc>().add(const BranchesStarted()),
              ),
            BranchesLoaded() => _Loaded(state: state),
          };
        },
      ),
      floatingActionButton: BlocBuilder<BranchesBloc, BranchesState>(
        builder: (context, state) {
          if (state is! BranchesLoaded || !state.company.isAdmin) {
            return const SizedBox.shrink();
          }
          return FloatingActionButton.extended(
            onPressed: state.adding ? null : () => _showAddSheet(context),
            label: Text(l10n.branchesAddCta),
            icon: const Icon(Icons.add),
          );
        },
      ),
    );
  }

  void _showAddSheet(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final nameCtrl = TextEditingController();
    final addressCtrl = TextEditingController();
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (sheetCtx) {
        return Padding(
          padding: EdgeInsets.only(
            left: AppSpacing.md,
            right: AppSpacing.md,
            top: AppSpacing.md,
            bottom: MediaQuery.of(sheetCtx).viewInsets.bottom + AppSpacing.md,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: nameCtrl,
                decoration: InputDecoration(labelText: l10n.branchNameLabel),
              ),
              const SizedBox(height: AppSpacing.md),
              TextField(
                controller: addressCtrl,
                decoration: InputDecoration(labelText: l10n.branchAddressLabel),
              ),
              const SizedBox(height: AppSpacing.md),
              AppButton(
                label: l10n.branchSaveCta,
                expand: true,
                onPressed: () {
                  if (nameCtrl.text.trim().isEmpty ||
                      addressCtrl.text.trim().isEmpty) {
                    return;
                  }
                  context.read<BranchesBloc>().add(BranchesAddRequested(
                        name: nameCtrl.text.trim(),
                        address: addressCtrl.text.trim(),
                      ));
                  Navigator.of(sheetCtx).pop();
                },
              ),
            ],
          ),
        );
      },
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final BranchesLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    if (state.company.branches.isEmpty) {
      return EmptyState(
        title: l10n.branchesEmpty,
        body: l10n.branchesEmpty,
        icon: Icons.account_tree_outlined,
      );
    }
    return RefreshIndicator(
      onRefresh: () async {
        final bloc = context.read<BranchesBloc>();
        bloc.add(const BranchesRefreshed());
        await bloc.stream.firstWhere((s) => s is BranchesLoaded).timeout(
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
          for (final b in state.company.branches)
            _BranchRow(
              branch: b,
              canManage: state.company.isAdmin,
              busy: state.busyBranchId == b.id,
            ),
        ],
      ),
    );
  }
}

class _BranchRow extends StatelessWidget {
  const _BranchRow({
    required this.branch,
    required this.canManage,
    required this.busy,
  });
  final Branch branch;
  final bool canManage;
  final bool busy;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: ListTile(
        title: Text(branch.name),
        subtitle: Text(branch.address),
        trailing: !canManage
            ? null
            : IconButton(
                icon: const Icon(Icons.delete_outline, color: AppColors.danger),
                onPressed: busy
                    ? null
                    : () async {
                        final confirm = await _confirmDelete(context);
                        if (!confirm) return;
                        if (!context.mounted) return;
                        context
                            .read<BranchesBloc>()
                            .add(BranchesDeleteRequested(branch.id));
                      },
                tooltip: l10n.branchesDeleteCta,
              ),
      ),
    );
  }

  Future<bool> _confirmDelete(BuildContext context) async {
    final l10n = AppLocalizations.of(context);
    final result = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(l10n.branchesDeleteConfirmTitle),
        content: Text(l10n.branchesDeleteConfirmBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: Text(l10n.commonCancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: Text(l10n.branchesDeleteCta),
          ),
        ],
      ),
    );
    return result == true;
  }
}
