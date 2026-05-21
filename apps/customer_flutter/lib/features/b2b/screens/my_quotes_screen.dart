import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/my_quotes_bloc.dart';
import '../data/models/quote_models.dart';
import '../widgets/quote_state_pill.dart';

/// S-8.1 — my quotes list. Mirrors MyReviewsScreen — filter chips,
/// pull-to-refresh, infinite scroll, row tap routes to detail.
class MyQuotesScreen extends StatefulWidget {
  const MyQuotesScreen({super.key});

  @override
  State<MyQuotesScreen> createState() => _MyQuotesScreenState();
}

class _MyQuotesScreenState extends State<MyQuotesScreen> {
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
        context.read<MyQuotesBloc>().add(const MyQuotesPageRequested());
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
      appBar: AppBar(title: Text(l10n.quotesListTitle)),
      body: BlocBuilder<MyQuotesBloc, MyQuotesState>(
        builder: (context, state) {
          return Column(
            children: [
              _FilterChips(activeState: state.filter.state),
              Expanded(
                child: switch (state) {
                  MyQuotesLoading() =>
                    LoadingState(semanticsLabel: l10n.commonLoading),
                  MyQuotesEmpty() => EmptyState(
                      title: l10n.quotesListEmpty,
                      body: l10n.quotesListEmptyBody,
                      icon: Icons.request_quote_outlined,
                    ),
                  MyQuotesLoaded() => RefreshIndicator(
                      onRefresh: () async {
                        final bloc = context.read<MyQuotesBloc>();
                        bloc.add(const MyQuotesRefreshed());
                        await bloc.stream
                            .firstWhere((s) => s is! MyQuotesLoading)
                            .timeout(
                              const Duration(seconds: 10),
                              onTimeout: () => bloc.state,
                            );
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
                  MyQuotesFailure() => ErrorState(
                      title: l10n.commonErrorTitle,
                      body: l10n.commonErrorBody,
                      retryLabel: l10n.commonRetry,
                      onRetry: () => context
                          .read<MyQuotesBloc>()
                          .add(const MyQuotesRefreshed()),
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
      (null, l10n.quotesListFilterAll),
      ('draft', l10n.quotesListFilterDraft),
      ('awaiting_acceptance', l10n.quotesListFilterAwaiting),
      ('accepted', l10n.quotesListFilterAccepted),
      ('rejected', l10n.quotesListFilterRejected),
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
                  .read<MyQuotesBloc>()
                  .add(MyQuotesFilterChanged(value)),
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
  final QuoteListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/quotes/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      item.quoteNumber,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  QuoteStatePill(state: item.state),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.quotesListCreatedAt(
                    dateFmt.format(item.createdAt.toLocal())),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (item.expiresAt != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.quotesListExpiresOn(
                    dateFmt.format(item.expiresAt!.toLocal()),
                  ),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
              if (item.totals != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.quotesListTotal(
                    _money(context, item.totals!.amount, item.totals!.currency),
                  ),
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  String _money(BuildContext context, String raw, String currency) {
    // Don't coerce unparseable totals to 0 — that would show a
    // fabricated zero to the user. Pass through the raw value verbatim
    // so the screen never shows a fake total.
    final parsed = double.tryParse(raw);
    if (parsed == null) return '$raw $currency';
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: currency,
      decimalDigits: 2,
    );
    return fmt.format(parsed);
  }
}
