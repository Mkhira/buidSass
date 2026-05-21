import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/my_review_detail_bloc.dart';
import '../widgets/review_state_pill.dart';
import '../widgets/stars_input.dart';

/// S-7.7 — my review detail + edit. Edit CTA disabled when the
/// `editableUntil` window has closed (BR-10).
class MyReviewDetailScreen extends StatelessWidget {
  const MyReviewDetailScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.myReviewDetailTitle)),
      body: BlocBuilder<MyReviewDetailBloc, MyReviewDetailState>(
        builder: (context, state) {
          return switch (state) {
            MyReviewDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            MyReviewDetailLoadFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<MyReviewDetailBloc>()
                    .add(const MyReviewDetailStarted()),
              ),
            MyReviewDetailLoaded() => _Loaded(state: state, saving: false),
            MyReviewDetailSaving(:final loaded) =>
              _Loaded(state: loaded, saving: true),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state, required this.saving});
  final MyReviewDetailLoaded state;
  final bool saving;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    final detail = state.detail;

    return SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          if (state.saveError != null)
            Container(
              width: double.infinity,
              margin: const EdgeInsets.only(bottom: AppSpacing.md),
              padding: const EdgeInsets.all(AppSpacing.md),
              decoration: BoxDecoration(
                color: AppColors.danger.withValues(alpha: 0.1),
                border: Border.all(color: AppColors.danger),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                state.saveError!,
                style: const TextStyle(color: AppColors.danger),
              ),
            ),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          detail.productName,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      ReviewStatePill(state: detail.state),
                    ],
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  StarsInput(
                    value: state.rating,
                    size: state.editing ? 32 : 20,
                    onChanged: state.editing && !saving
                        ? (v) => context
                            .read<MyReviewDetailBloc>()
                            .add(MyReviewDetailRatingChanged(v))
                        : null,
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  if (state.editing)
                    TextFormField(
                      initialValue: state.comment,
                      decoration: InputDecoration(
                        helperText: '${state.comment.length}/2000',
                      ),
                      maxLines: 6,
                      maxLength: 2000,
                      onChanged: saving
                          ? null
                          : (v) => context
                              .read<MyReviewDetailBloc>()
                              .add(MyReviewDetailCommentChanged(v)),
                    )
                  else
                    Text(
                      state.comment,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  const SizedBox(height: AppSpacing.sm),
                  Text(
                    dateFmt.format(detail.createdAt),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  if (detail.moderationNote != null &&
                      detail.moderationNote!.isNotEmpty) ...[
                    const SizedBox(height: AppSpacing.md),
                    Text(
                      l10n.myReviewDetailModerationNoteLabel,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    const SizedBox(height: AppSpacing.xs),
                    Text(
                      detail.moderationNote!,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ],
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          if (state.editing) ...[
            AppButton(
              label: l10n.myReviewDetailSaveCta,
              expand: true,
              isLoading: saving,
              onPressed: saving || !state.canSave
                  ? null
                  : () => context
                      .read<MyReviewDetailBloc>()
                      .add(const MyReviewDetailSaved()),
            ),
            const SizedBox(height: AppSpacing.sm),
            TextButton(
              onPressed: saving
                  ? null
                  : () => context
                      .read<MyReviewDetailBloc>()
                      .add(const MyReviewDetailEditToggled()),
              child: Text(l10n.commonCancel),
            ),
          ] else
            AppButton(
              label: state.isEditableNow
                  ? l10n.myReviewDetailEditCta
                  : l10n.myReviewDetailEditWindowClosed,
              expand: true,
              onPressed: state.isEditableNow
                  ? () => context
                      .read<MyReviewDetailBloc>()
                      .add(const MyReviewDetailEditToggled())
                  : null,
            ),
        ],
      ),
    );
  }
}
