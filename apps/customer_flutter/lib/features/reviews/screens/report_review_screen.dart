import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/report_review_bloc.dart';

/// S-7.8 — report someone else's review. Reasons from server (BR-9).
class ReportReviewScreen extends StatelessWidget {
  const ReportReviewScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.reportReviewTitle)),
      body: BlocConsumer<ReportReviewBloc, ReportReviewState>(
        listener: (context, state) {
          if (state is ReportReviewDone) {
            final messenger = ScaffoldMessenger.maybeOf(context);
            messenger?.showSnackBar(SnackBar(
              content: Text(l10n.reportReviewThanks),
            ));
            context.pop();
          }
        },
        builder: (context, state) {
          return switch (state) {
            ReportReviewLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ReportReviewLoadFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<ReportReviewBloc>()
                    .add(const ReportReviewStarted()),
              ),
            ReportReviewReady() => _Form(state: state, submitting: false),
            ReportReviewSubmitting(:final ready) =>
              _Form(state: ready, submitting: true),
            ReportReviewDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.state, required this.submitting});
  final ReportReviewReady state;
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
                  state.formError!,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Text(
                  l10n.reportReviewReasonLabel,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.sm),
                RadioGroup<String>(
                  groupValue: state.selectedReason,
                  onChanged: (v) {
                    if (submitting) return;
                    if (v != null) {
                      context
                          .read<ReportReviewBloc>()
                          .add(ReportReviewReasonSelected(v));
                    }
                  },
                  child: Column(
                    children: [
                      for (final r in state.reasons)
                        RadioListTile<String>(
                          value: r.key,
                          title: Text(r.label),
                        ),
                    ],
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                TextFormField(
                  initialValue: state.note,
                  decoration: InputDecoration(
                    labelText: l10n.reportReviewNoteLabel,
                  ),
                  maxLines: 3,
                  onChanged: submitting
                      ? null
                      : (v) => context
                          .read<ReportReviewBloc>()
                          .add(ReportReviewNoteChanged(v)),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.reportReviewSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting || !state.canSubmit
                  ? null
                  : () => context
                      .read<ReportReviewBloc>()
                      .add(const ReportReviewSubmitted()),
            ),
          ),
        ],
      ),
    );
  }
}
