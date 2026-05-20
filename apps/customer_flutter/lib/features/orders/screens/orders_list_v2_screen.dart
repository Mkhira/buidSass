import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/orders_list_v2_bloc.dart';
import '../data/models/order_models.dart';
import '../widgets/state_pills.dart';

class OrdersListV2Screen extends StatefulWidget {
  const OrdersListV2Screen({super.key});

  @override
  State<OrdersListV2Screen> createState() => _OrdersListV2ScreenState();
}

class _OrdersListV2ScreenState extends State<OrdersListV2Screen> {
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
        context.read<OrdersListV2Bloc>().add(const OrdersListPageRequested());
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
      appBar: AppBar(title: Text(l10n.ordersTitle)),
      body: BlocBuilder<OrdersListV2Bloc, OrdersListV2State>(
        builder: (context, state) {
          return Column(
            children: [
              _FilterChips(activeStatus: state.filter.status),
              Expanded(
                child: switch (state) {
                  OrdersListLoading() =>
                    LoadingState(semanticsLabel: l10n.commonLoading),
                  OrdersListEmpty() => EmptyState(title: l10n.ordersEmpty),
                  OrdersListLoaded() => RefreshIndicator(
                      onRefresh: () async => context
                          .read<OrdersListV2Bloc>()
                          .add(const OrdersListRefreshed()),
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
                          return _OrderRow(item: state.items[i]);
                        },
                      ),
                    ),
                  OrdersListFailure(:final reason) => ErrorState(
                      title: l10n.commonErrorTitle,
                      body: reason,
                      onRetry: () => context
                          .read<OrdersListV2Bloc>()
                          .add(const OrdersListRefreshed()),
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
    // server-driven enum the gateway sends as `status=`. The "All" chip
    // dispatches `null` to clear the filter.
    final chips = [
      (null, l10n.ordersFilterAll),
      ('pending', l10n.ordersFilterPending),
      ('shipped', l10n.ordersFilterShipped),
      ('delivered', l10n.ordersFilterDelivered),
      ('cancelled', l10n.ordersFilterCancelled),
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
                  .read<OrdersListV2Bloc>()
                  .add(OrdersListFilterChanged(value)),
            ),
            const SizedBox(width: AppSpacing.xs),
          ],
        ],
      ),
    );
  }
}

class _OrderRow extends StatelessWidget {
  const _OrderRow({required this.item});
  final OrderListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: item.totals.currency,
      decimalDigits: 2,
    );
    return Card(
      child: InkWell(
        onTap: () => context.push('/o/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(item.orderNumber,
                        style: Theme.of(context).textTheme.titleSmall),
                  ),
                  Text(
                      fmt.format(double.tryParse(item.totals.grandTotal) ?? 0)),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.ordersListPlacedAt(dateFmt.format(item.placedAt)),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: AppSpacing.sm),
              StatePillsRow(states: item.states),
            ],
          ),
        ),
      ),
    );
  }
}
