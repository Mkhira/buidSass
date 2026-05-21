import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/company_profile_bloc.dart';
import '../widgets/role_picker.dart';

class CompanyProfileScreen extends StatelessWidget {
  const CompanyProfileScreen({super.key, required this.companyId});
  final String companyId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.companyProfileTitle)),
      body: BlocBuilder<CompanyProfileBloc, CompanyProfileState>(
        builder: (context, state) {
          return switch (state) {
            CompanyProfileLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CompanyProfileLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<CompanyProfileBloc>()
                    .add(const CompanyProfileStarted()),
              ),
            CompanyProfileLoaded() => _Loaded(state: state, saving: false),
            CompanyProfileSaving(:final loaded) =>
              _Loaded(state: loaded, saving: true),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state, required this.saving});
  final CompanyProfileLoaded state;
  final bool saving;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final company = state.company;
    return RefreshIndicator(
      onRefresh: () async {
        final bloc = context.read<CompanyProfileBloc>();
        bloc.add(const CompanyProfileRefreshed());
        await bloc.stream.firstWhere((s) => s is CompanyProfileLoaded).timeout(
              const Duration(seconds: 10),
              onTimeout: () => bloc.state,
            );
      },
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          if (!company.isAdmin)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.md),
              child: Container(
                padding: const EdgeInsets.all(AppSpacing.sm),
                color: AppColors.warning.withValues(alpha: 0.08),
                child: Text(
                  l10n.companyProfileReadOnlyHint,
                  style: Theme.of(context)
                      .textTheme
                      .bodySmall
                      ?.copyWith(color: AppColors.warning),
                ),
              ),
            ),
          if (state.saveError != null)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.md),
              child: Container(
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
            ),
          Text(
            l10n.companyProfileSectionProfile,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: AppSpacing.sm),
          _field(
            context,
            label: l10n.companyNameLabel,
            value: state.draft.name ?? company.name,
            editable: state.editing,
            saving: saving,
            onChanged: (v) => _emit(context, 'name', v),
          ),
          const SizedBox(height: AppSpacing.md),
          _field(
            context,
            label: l10n.companyVatNumberLabel,
            value: state.draft.vatNumber ?? company.vatNumber,
            editable: state.editing,
            saving: saving,
            onChanged: (v) => _emit(context, 'vatNumber', v),
          ),
          const SizedBox(height: AppSpacing.md),
          _field(
            context,
            label: l10n.companyAddressLabel,
            value: state.draft.address ?? company.address,
            editable: state.editing,
            saving: saving,
            onChanged: (v) => _emit(context, 'address', v),
          ),
          const SizedBox(height: AppSpacing.md),
          _field(
            context,
            label: l10n.companyCommercialRegistrationLabel,
            value: state.draft.commercialRegistration ??
                (company.commercialRegistration ?? ''),
            editable: state.editing,
            saving: saving,
            onChanged: (v) => _emit(context, 'commercialRegistration', v),
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.companyProfileSectionBranches,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: AppSpacing.sm),
          for (final b in company.branches)
            Card(
              child: ListTile(
                title: Text(b.name),
                subtitle: Text(b.address),
              ),
            ),
          if (company.isAdmin) ...[
            const SizedBox(height: AppSpacing.sm),
            OutlinedButton(
              onPressed: () => context.push('/company/${company.id}/branches'),
              child: Text(l10n.branchesTitle),
            ),
          ],
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.companyProfileSectionMembers,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: AppSpacing.sm),
          for (final m in company.memberships)
            Card(
              child: ListTile(
                title: Text(m.name),
                subtitle: Text(roleLabel(context, m.role)),
              ),
            ),
          if (company.isAdmin) ...[
            const SizedBox(height: AppSpacing.sm),
            OutlinedButton(
              onPressed: () => context.push('/company/${company.id}/members'),
              child: Text(l10n.membershipsTitle),
            ),
            const SizedBox(height: AppSpacing.sm),
            OutlinedButton(
              onPressed: () =>
                  context.push('/company/${company.id}/invitations/new'),
              child: Text(l10n.inviteUserTitle),
            ),
          ],
          const SizedBox(height: AppSpacing.lg),
          if (company.isAdmin)
            state.editing
                ? Row(
                    children: [
                      Expanded(
                        child: AppButton(
                          label: l10n.companyProfileSaveCta,
                          expand: true,
                          isLoading: saving,
                          onPressed: saving
                              ? null
                              : () => context
                                  .read<CompanyProfileBloc>()
                                  .add(const CompanyProfileSaved()),
                        ),
                      ),
                      const SizedBox(width: AppSpacing.sm),
                      TextButton(
                        onPressed: saving
                            ? null
                            : () => context
                                .read<CompanyProfileBloc>()
                                .add(const CompanyProfileEditToggled()),
                        child: Text(l10n.commonCancel),
                      ),
                    ],
                  )
                : OutlinedButton(
                    onPressed: () => context
                        .read<CompanyProfileBloc>()
                        .add(const CompanyProfileEditToggled()),
                    child: Text(l10n.companyProfileSaveCta),
                  ),
        ],
      ),
    );
  }

  void _emit(BuildContext context, String key, String value) {
    context
        .read<CompanyProfileBloc>()
        .add(CompanyProfileFieldChanged(key: key, value: value));
  }

  Widget _field(
    BuildContext context, {
    required String label,
    required String value,
    required bool editable,
    required bool saving,
    required ValueChanged<String> onChanged,
  }) {
    if (!editable) {
      return Card(
        child: ListTile(
          title: Text(label, style: Theme.of(context).textTheme.bodySmall),
          subtitle: Text(value),
        ),
      );
    }
    return TextFormField(
      initialValue: value,
      enabled: !saving,
      decoration: InputDecoration(labelText: label),
      onChanged: onChanged,
    );
  }
}
