import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/order_list_bloc.dart';
import '../widgets/state_stream_chips.dart';

class OrdersListScreen extends StatefulWidget {
  const OrdersListScreen({super.key});

  @override
  State<OrdersListScreen> createState() => _OrdersListScreenState();
}

class _OrdersListScreenState extends State<OrdersListScreen> {
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
        context.read<OrderListBloc>().add(const OrderListPageRequested());
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
    return Scaffold(
      appBar: AppBar(title: Text(l10n.navOrders)),
      body: BlocBuilder<OrderListBloc, OrderListState>(
        builder: (context, state) {
          return switch (state) {
            OrderListIdle() ||
            OrderListLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            OrderListEmpty() => EmptyState(title: l10n.ordersEmpty),
            OrderListError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context
                    .read<OrderListBloc>()
                    .add(const OrderListRefreshTapped()),
                retryLabel: l10n.commonRetry,
              ),
            OrderListLoaded(:final items, :final isLoadingMore) =>
              RefreshIndicator(
                onRefresh: () async => context
                    .read<OrderListBloc>()
                    .add(const OrderListRefreshTapped()),
                child: ListView.builder(
                  controller: _scroll,
                  itemCount: items.length + (isLoadingMore ? 1 : 0),
                  itemBuilder: (ctx, i) {
                    if (i == items.length) {
                      return const Padding(
                        padding: EdgeInsets.all(AppSpacing.md),
                        child: Center(child: CircularProgressIndicator()),
                      );
                    }
                    final o = items[i];
                    return ListTile(
                      title: Text(o.orderNumber),
                      subtitle: Padding(
                        padding: const EdgeInsets.only(top: AppSpacing.xs),
                        child: StateStreamChips(
                          orderState: o.orderState,
                          paymentState: o.paymentState,
                          fulfillmentState: o.fulfillmentState,
                          refundState: o.refundState,
                        ),
                      ),
                      onTap: () => context.go('/o/${o.id}'),
                    );
                  },
                ),
              ),
          };
        },
      ),
    );
  }
}
