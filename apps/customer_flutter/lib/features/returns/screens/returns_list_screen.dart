import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/returns_list_bloc.dart';
import '../data/models/return_models.dart';
import '../widgets/return_state_pill.dart';

/// S-6.1 — customer returns list. Mirrors S-5.1 (orders list) for
/// consistency: filter chips on top, paginated list with pull-to-refresh
/// and infinite scroll, loading / empty / error states out of the
/// design system.
class ReturnsListScreen extends StatefulWidget {
  const ReturnsListScreen({super.key});

  @override
  State<ReturnsListScreen> createState() => _ReturnsListScreenState();
}

class _ReturnsListScreenState extends State<ReturnsListScreen> {
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
        context.read<ReturnsListBloc>().add(const ReturnsListPageRequested());
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
      appBar: AppBar(title: Text(l10n.returnsTitle)),
      body: BlocBuilder<ReturnsListBloc, ReturnsListState>(
        builder: (context, state) {
          return Column(
            children: [
              _FilterChips(activeStatus: state.filter.status),
              Expanded(
                child: switch (state) {
                  ReturnsListLoading() =>
                    LoadingState(semanticsLabel: l10n.commonLoading),
                  ReturnsListEmpty() => EmptyState(
                      title: l10n.returnsEmpty,
                      body: l10n.returnsEmptyBody,
                      icon: Icons.assignment_return_outlined,
                    ),
                  ReturnsListLoaded() => RefreshIndicator(
                      onRefresh: () async {
                        final bloc = context.read<ReturnsListBloc>();
                        bloc.add(const ReturnsListRefreshed());
                        // Wait until the bloc settles into a non-loading
                        // state so the spinner only hides once data has
                        // actually reloaded.
                        await bloc.stream
                            .firstWhere((s) => s is! ReturnsListLoading);
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
                          return _ReturnRow(item: state.items[i]);
                        },
                      ),
                    ),
                  ReturnsListFailure(:final reason) => ErrorState(
                      title: l10n.commonErrorTitle,
                      body: reason,
                      onRetry: () => context
                          .read<ReturnsListBloc>()
                          .add(const ReturnsListRefreshed()),
                      retryLabel: l10n.commonRetry,
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
  const _FilterChips({required this.activeStatus});
  final String? activeStatus;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    // Chip labels render via l10n, but the wire values stay the
    // server-driven state enum the gateway sends as `status=`. The
    // "All" chip dispatches `null` to clear the filter.
    final chips = <(String?, String)>[
      (null, l10n.returnsFilterAll),
      ('pending', l10n.returnsFilterPending),
      ('approved', l10n.returnsFilterApproved),
      ('issued', l10n.returnsFilterIssued),
      ('rejected', l10n.returnsFilterRejected),
    ];
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.md, vertical: AppSpacing.sm),
      child: Row(
        children: [
          for (final (value, label) in chips) ...[
            ChoiceChip(
              label: Text(label),
              selected: activeStatus == value,
              onSelected: (_) => context
                  .read<ReturnsListBloc>()
                  .add(ReturnsListFilterChanged(value)),
            ),
            const SizedBox(width: AppSpacing.xs),
          ],
        ],
      ),
    );
  }
}

class _ReturnRow extends StatelessWidget {
  const _ReturnRow({required this.item});
  final ReturnListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/returns/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(item.returnNumber,
                        style: Theme.of(context).textTheme.titleSmall),
                  ),
                  ReturnStatePill(state: item.state),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.returnsListOrderLabel(item.orderNumber),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.returnsListCreatedAt(dateFmt.format(item.createdAt)),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (item.refundAmount != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  l10n.returnsListRefundAmount(_money(
                    context,
                    item.refundAmount!.amount,
                    item.refundAmount!.currency,
                  )),
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: AppColors.success,
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
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: currency,
      decimalDigits: 2,
    );
    return fmt.format(double.tryParse(raw) ?? 0);
  }
}
