import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/my_reviews_bloc.dart';
import '../data/models/review_models.dart';
import '../widgets/review_state_pill.dart';
import '../widgets/stars_input.dart';

/// S-7.6 — list of the customer's own reviews. Mirrors ReturnsListScreen.
class MyReviewsScreen extends StatefulWidget {
  const MyReviewsScreen({super.key});

  @override
  State<MyReviewsScreen> createState() => _MyReviewsScreenState();
}

class _MyReviewsScreenState extends State<MyReviewsScreen> {
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
        context.read<MyReviewsBloc>().add(const MyReviewsPageRequested());
      }
    });
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.myReviewsTitle)),
      body: BlocBuilder<MyReviewsBloc, MyReviewsState>(
        builder: (context, state) {
          return Column(
            children: [
              _FilterChips(activeState: state.filter.state),
              Expanded(
                child: switch (state) {
                  MyReviewsLoading() =>
                    LoadingState(semanticsLabel: l10n.commonLoading),
                  MyReviewsEmpty() => EmptyState(
                      title: l10n.myReviewsEmptyTitle,
                      body: l10n.myReviewsEmptyBody,
                      icon: Icons.rate_review_outlined,
                    ),
                  MyReviewsLoaded() => RefreshIndicator(
                      onRefresh: () async {
                        final bloc = context.read<MyReviewsBloc>();
                        bloc.add(const MyReviewsRefreshed());
                        await bloc.stream
                            .firstWhere((s) => s is! MyReviewsLoading);
                      },
                      child: ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.all(AppSpacing.md),
                        itemCount:
                            state.items.length + (state.isLoadingMore ? 1 : 0),
                        itemBuilder: (context, i) {
                          if (i >= state.items.length) {
                            return const Padding(
                              padding: EdgeInsets.all(AppSpacing.md),
                              child: Center(child: CircularProgressIndicator()),
                            );
                          }
                          return _Row(item: state.items[i]);
                        },
                      ),
                    ),
                  MyReviewsFailure(:final reason) => ErrorState(
                      title: l10n.commonErrorTitle,
                      body: reason,
                      retryLabel: l10n.commonRetry,
                      onRetry: () => context
                          .read<MyReviewsBloc>()
                          .add(const MyReviewsRefreshed()),
                    ),
                },
              ),
            ],
          );
        },
      ),
    );
  }
}

class _FilterChips extends StatelessWidget {
  const _FilterChips({required this.activeState});
  final String? activeState;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final chips = <(String?, String)>[
      (null, l10n.myReviewsFilterAll),
      ('pending_moderation', l10n.myReviewsFilterPending),
      ('visible', l10n.myReviewsFilterVisible),
      ('hidden', l10n.myReviewsFilterHidden),
    ];
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          for (final (value, label) in chips) ...[
            ChoiceChip(
              label: Text(label),
              selected: activeState == value,
              onSelected: (_) => context
                  .read<MyReviewsBloc>()
                  .add(MyReviewsFilterChanged(value)),
            ),
            const SizedBox(width: AppSpacing.xs),
          ],
        ],
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.item});
  final MyReviewListItem item;

  @override
  Widget build(BuildContext context) {
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/my-reviews/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      item.productName,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  ReviewStatePill(state: item.state),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              StarsInput(value: item.rating, size: 16),
              const SizedBox(height: AppSpacing.xs),
              Text(
                dateFmt.format(item.createdAt),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
