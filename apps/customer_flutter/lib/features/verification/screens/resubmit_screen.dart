import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/resubmit_cubit.dart';

/// S-7.4 resubmit. Only requested-info fields are editable; the rest
/// of the case is read-only for context. Idempotency-Key is locked in
/// at cubit construction.
class ResubmitScreen extends StatelessWidget {
  const ResubmitScreen({super.key, required this.verificationId});
  final String verificationId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.verificationResubmitTitle)),
      body: BlocConsumer<ResubmitCubit, ResubmitState>(
        listener: (context, state) {
          if (state is ResubmitDone) {
            context.go('/verification/${state.detail.id}');
          }
        },
        builder: (context, state) {
          return switch (state) {
            ResubmitLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ResubmitFailureLoad(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                retryLabel: l10n.commonRetry,
                onRetry: () => context.read<ResubmitCubit>().load(),
              ),
            ResubmitForm() => _Form(state: state, submitting: false),
            ResubmitSubmitting(:final form) =>
              _Form(state: form, submitting: true),
            ResubmitDone() => LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.state, required this.submitting});
  final ResubmitForm state;
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
              child: _ErrorBanner(
                  message: _resolveFormError(l10n, state.formError!)),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                if (state.editableFields.isEmpty)
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Text(
                      l10n.verificationDetailRequestedInfoTitle,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ),
                for (final ri in state.editableFields) ...[
                  TextFormField(
                    initialValue: state.values[ri.key]?.toString() ?? '',
                    decoration: InputDecoration(
                      labelText: ri.key,
                      helperText: ri.note,
                    ),
                    onChanged: submitting
                        ? null
                        : (v) => context
                            .read<ResubmitCubit>()
                            .fieldChanged(ri.key, v),
                  ),
                  const SizedBox(height: AppSpacing.md),
                ],
                TextFormField(
                  initialValue: state.note,
                  decoration: InputDecoration(
                    labelText: l10n.verificationResubmitNoteLabel,
                  ),
                  maxLines: 3,
                  onChanged: submitting
                      ? null
                      : (v) => context.read<ResubmitCubit>().noteChanged(v),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.verificationResubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting
                  ? null
                  : () => context.read<ResubmitCubit>().submit(),
            ),
          ),
        ],
      ),
    );
  }

  String _resolveFormError(AppLocalizations l10n, String key) {
    if (key == 'verificationSubmitErrorMissingRequired') {
      return l10n.verificationSubmitErrorMissingRequired;
    }
    // Unknown error code — fall back to a generic localized message
    // rather than echoing the raw code/exception text to the user.
    return l10n.commonErrorBody;
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
