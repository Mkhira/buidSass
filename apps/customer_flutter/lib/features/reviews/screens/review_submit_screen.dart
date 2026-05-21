import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/review_submit_bloc.dart';
import '../widgets/stars_input.dart';

/// S-7.5 — submit a review. Stars + comment + locale selector.
/// 403 from server (verified-buyer gate per BR-6) flips to a friendly
/// "Only verified buyers can review" empty state with a link back to
/// orders.
class ReviewSubmitScreen extends StatelessWidget {
  const ReviewSubmitScreen({
    super.key,
    required this.productId,
    required this.orderId,
  });

  final String productId;
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.reviewSubmitTitle)),
      body: BlocConsumer<ReviewSubmitBloc, ReviewSubmitState>(
        listener: (context, state) {
          if (state is ReviewSubmitDone) {
            final messenger = ScaffoldMessenger.maybeOf(context);
            messenger?.showSnackBar(SnackBar(
              content: Text(l10n.reviewSubmitSubmittedTitle),
            ));
            context.go('/my-reviews');
          }
        },
        builder: (context, state) {
          // Use the sealed type's exhaustive switch so we never reach an
          // unsafe `as ReviewSubmitForm` cast. `ReviewSubmitDone` shows
          // a loading shell because the listener navigates async — the
          // builder still runs once on that state before the route
          // changes.
          return switch (state) {
            ReviewSubmitNotEligible() => Column(
              children: [
                Expanded(
                  child: EmptyState(
                    title: l10n.reviewSubmitNotEligibleTitle,
                    body: l10n.reviewSubmitNotEligibleBody,
                    icon: Icons.lock_outlined,
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  child: AppButton(
                    label: l10n.navOrders,
                    expand: true,
                    onPressed: () => context.go('/orders'),
                  ),
                ),
              ],
            ),
            ReviewSubmitForm() => _Form(form: state, submitting: false),
            ReviewSubmitSubmitting(:final form) =>
              _Form(form: form, submitting: true),
            ReviewSubmitDone() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          };
        },
      ),
    );
  }
}

class _Form extends StatelessWidget {
  const _Form({required this.form, required this.submitting});
  final ReviewSubmitForm form;
  final bool submitting;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return SafeArea(
      child: Column(
        children: [
          if (form.formError != null)
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
                  form.formError!,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Text(
                  l10n.reviewSubmitRatingLabel,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.sm),
                StarsInput(
                  value: form.rating,
                  onChanged: submitting
                      ? null
                      : (v) => context
                          .read<ReviewSubmitBloc>()
                          .add(ReviewSubmitRatingChanged(v)),
                ),
                const SizedBox(height: AppSpacing.lg),
                Text(
                  l10n.reviewSubmitCommentLabel,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.sm),
                TextFormField(
                  initialValue: form.comment,
                  decoration: InputDecoration(
                    hintText: l10n.reviewSubmitCommentHint,
                    helperText: '${form.comment.length}/2000',
                  ),
                  maxLines: 6,
                  maxLength: 2000,
                  onChanged: submitting
                      ? null
                      : (v) => context
                          .read<ReviewSubmitBloc>()
                          .add(ReviewSubmitCommentChanged(v)),
                ),
                const SizedBox(height: AppSpacing.md),
                Text(
                  l10n.reviewSubmitLocaleLabel,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: AppSpacing.sm),
                SegmentedButton<String>(
                  segments: const [
                    ButtonSegment(value: 'en', label: Text('EN')),
                    ButtonSegment(value: 'ar', label: Text('AR')),
                  ],
                  selected: {form.locale},
                  onSelectionChanged: submitting
                      ? null
                      : (s) => context
                          .read<ReviewSubmitBloc>()
                          .add(ReviewSubmitLocaleChanged(s.first)),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: AppButton(
              label: l10n.reviewSubmitCta,
              expand: true,
              isLoading: submitting,
              onPressed: submitting || !form.canSubmit
                  ? null
                  : () => context
                      .read<ReviewSubmitBloc>()
                      .add(const ReviewSubmitSubmitted()),
            ),
          ),
        ],
      ),
    );
  }
}
