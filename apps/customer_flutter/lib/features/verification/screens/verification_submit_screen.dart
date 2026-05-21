import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/verification_submit_bloc.dart';
import '../data/models/verification_models.dart';
import '../widgets/schema_field_widget.dart';

/// S-7.2 — submit verification. Renders a form dynamically from the
/// server-supplied schema. Document slots are surfaced as hints here;
/// actual uploads happen on the detail screen (S-7.3) after the case
/// is created.
class VerificationSubmitScreen extends StatelessWidget {
  const VerificationSubmitScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.verificationSubmitTitle)),
      body: BlocConsumer<VerificationSubmitBloc, VerificationSubmitState>(
        listener: (context, state) {
          if (state is VerificationSubmitDone) {
            context.go('/verification/${state.result.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            VerificationSubmitSchemaLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            VerificationSubmitSchemaFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                retryLabel: l10n.commonRetry,
                onRetry: () => Navigator.of(context).maybePop(),
              ),
            VerificationSubmitForm() => _FormView(state: state, submitting: false),
            VerificationSubmitSubmitting(:final form) =>
              _FormView(state: form, submitting: true),
            VerificationSubmitDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _FormView extends StatelessWidget {
  const _FormView({required this.state, required this.submitting});
  final VerificationSubmitForm state;
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
                child: Text(
                  _resolveFormError(l10n, state.formError!),
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                for (final field in state.schema.fields) ...[
                  SchemaFieldWidget(
                    field: field,
                    value: _read(state.values, field),
                    errorKey: state.fieldErrors[field.key],
                    onChanged: submitting
                        ? (_) {}
                        : (v) => context
                            .read<VerificationSubmitBloc>()
                            .add(VerificationSubmitFieldChanged(
                              key: field.key,
                              value: v,
                            )),
                  ),
                  const SizedBox(height: AppSpacing.md),
                ],
                if (state.schema.documentSlots.isNotEmpty) ...[
                  Text(
                    l10n.verificationDetailDocumentsLabel,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  for (final slot in state.schema.documentSlots)
                    Card(
                      color: AppColors.neutral,
                      child: Padding(
                        padding: const EdgeInsets.all(AppSpacing.md),
                        child: Row(
                          children: [
                            const Icon(Icons.upload_file_outlined, size: 20),
                            const SizedBox(width: AppSpacing.sm),
                            Expanded(
                              child: Text(
                                slot.label,
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                            ),
                            if (slot.required)
                              Text(
                                l10n.verificationSubmitRequiredHint,
                                style: Theme.of(context)
                                    .textTheme
                                    .bodySmall
                                    ?.copyWith(color: AppColors.warning),
                              ),
                          ],
                        ),
                      ),
                    ),
                ],
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.verificationSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting
                  ? null
                  : () => context
                      .read<VerificationSubmitBloc>()
                      .add(const VerificationSubmitSubmitted()),
            ),
          ),
        ],
      ),
    );
  }

  /// Decode the value stored in the form map. Date fields are stored
  /// as ISO-8601 strings (data-model.md uses string transport for date
  /// fields); convert back to `DateTime` for the date picker widget.
  Object? _read(Map<String, Object?> values, SchemaField field) {
    final raw = values[field.key];
    if (field.type == 'date' && raw is String) return DateTime.tryParse(raw);
    return raw;
  }

  String _resolveFormError(AppLocalizations l10n, String key) {
    if (key == 'verificationSubmitErrorMissingRequired') {
      return l10n.verificationSubmitErrorMissingRequired;
    }
    return key;
  }
}
