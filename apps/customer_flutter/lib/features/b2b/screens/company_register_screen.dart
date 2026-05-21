import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/company_register_bloc.dart';

class CompanyRegisterScreen extends StatelessWidget {
  const CompanyRegisterScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.companyRegisterTitle)),
      body: BlocConsumer<CompanyRegisterBloc, CompanyRegisterState>(
        listener: (context, state) {
          if (state is CompanyRegisterDone) {
            context.go('/company/${state.result.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            CompanyRegisterForm() => _Form(state: state, submitting: false),
            CompanyRegisterSubmitting(:final form) =>
              _Form(state: form, submitting: true),
            CompanyRegisterDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.state, required this.submitting});
  final CompanyRegisterForm state;
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
              child: _ErrorBanner(message: l10n.commonErrorBody),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                _Field(
                  label: l10n.companyNameLabel,
                  initial: state.name,
                  enabled: !submitting,
                  onChanged: (v) => _emit(context, 'name', v),
                ),
                const SizedBox(height: AppSpacing.md),
                _Field(
                  label: l10n.companyVatNumberLabel,
                  initial: state.vatNumber,
                  enabled: !submitting,
                  onChanged: (v) => _emit(context, 'vatNumber', v),
                ),
                const SizedBox(height: AppSpacing.md),
                _Field(
                  label: l10n.companyAddressLabel,
                  initial: state.address,
                  enabled: !submitting,
                  onChanged: (v) => _emit(context, 'address', v),
                ),
                const SizedBox(height: AppSpacing.md),
                _Field(
                  label: l10n.companyCommercialRegistrationLabel,
                  initial: state.commercialRegistration,
                  enabled: !submitting,
                  onChanged: (v) => _emit(context, 'commercialRegistration', v),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.companyRegisterSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting || !state.canSubmit
                  ? null
                  : () => context
                      .read<CompanyRegisterBloc>()
                      .add(const CompanyRegisterSubmitted()),
            ),
          ),
        ],
      ),
    );
  }

  void _emit(BuildContext context, String key, String value) {
    context
        .read<CompanyRegisterBloc>()
        .add(CompanyRegisterFieldChanged(key: key, value: value));
  }
}

class _Field extends StatelessWidget {
  const _Field({
    required this.label,
    required this.initial,
    required this.onChanged,
    this.enabled = true,
  });
  final String label;
  final String initial;
  final ValueChanged<String> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      initialValue: initial,
      enabled: enabled,
      decoration: InputDecoration(labelText: label),
      onChanged: onChanged,
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.danger.withValues(alpha: 0.1),
        border: Border.all(color: AppColors.danger),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(message, style: const TextStyle(color: AppColors.danger)),
    );
  }
}
