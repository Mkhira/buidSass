import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/order_detail_v2_bloc.dart';
import '../data/models/order_models.dart';
import '../widgets/state_pills.dart';
import '../widgets/tracking_timeline.dart';

class OrderDetailV2Screen extends StatelessWidget {
  const OrderDetailV2Screen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.orderDetailTitle)),
      body: BlocBuilder<OrderDetailV2Bloc, OrderDetailV2State>(
        builder: (context, state) {
          return switch (state) {
            OrderDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            OrderDetailFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<OrderDetailV2Bloc>()
                    .add(const OrderDetailRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            OrderDetailLoaded(:final order, :final eligibility) =>
              RefreshIndicator(
                onRefresh: () async {
                  final bloc = context.read<OrderDetailV2Bloc>();
                  bloc.add(const OrderDetailRefreshed());
                  // Wait until the bloc settles into a non-loading
                  // state so the pull-to-refresh spinner only hides
                  // once data has actually reloaded.
                  await bloc.stream.firstWhere((s) => s is! OrderDetailLoading);
                },
                child: _LoadedBody(
                    order: order, eligibility: eligibility, orderId: orderId),
              ),
          };
        },
      ),
    );
  }
}

class _LoadedBody extends StatelessWidget {
  const _LoadedBody({
    required this.order,
    required this.eligibility,
    required this.orderId,
  });
  final OrderDetail order;
  final ReturnEligibility? eligibility;
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: order.totals.currency,
      decimalDigits: 2,
    );
    String money(String raw) => fmt.format(double.tryParse(raw) ?? 0);
    // Return CTA is gated by BOTH the server's `actions.canReturn` AND
    // the eligibility call's `anyEligible` (when present). BR-2 keeps
    // the decision server-side.
    final canReturn = order.actions.canReturn &&
        (eligibility?.anyEligible ?? order.actions.canReturn);
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        Text(order.orderNumber, style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: AppSpacing.sm),
        StatePillsRow(states: order.states),
        const SizedBox(height: AppSpacing.md),
        _ActionsToolbar(
          order: order,
          canReturn: canReturn,
          onCancel: () => context.push('/o/$orderId/cancel'),
          onReorder: () => context.push('/o/$orderId/reorder'),
          onReturn: () => context.push('/o/$orderId/return'),
          onRetry: () => context.go('/checkout'),
          onTrack: () async {
            final url = order.shipment.tracking?.url;
            if (url == null || url.isEmpty) return;
            await launchUrl(Uri.parse(url),
                mode: LaunchMode.externalApplication);
          },
        ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.orderDetailLines),
        for (final line in order.lines)
          ListTile(
            title: Text(line.name),
            subtitle: Text('${line.qty} × ${money(line.unitPrice)}'),
            trailing: Text(money(line.lineTotal)),
          ),
        if (order.address != null) ...[
          const Divider(height: AppSpacing.lg),
          _SectionTitle(text: l10n.orderDetailShipping),
          ListTile(
            leading: const Icon(Icons.location_on_outlined),
            title: Text(order.address!.name),
            subtitle: Text('${order.address!.street}, ${order.address!.city}'),
          ),
          if (order.shipment.tracking != null)
            ListTile(
              leading: const Icon(Icons.local_shipping_outlined),
              title: Text(
                  '${order.shipment.tracking!.carrier} · ${order.shipment.tracking!.trackingNumber}'),
            ),
          TrackingTimeline(events: order.shipment.events),
        ],
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.orderDetailPayment),
        ListTile(
          leading: const Icon(Icons.payment_outlined),
          title: Text(order.payment.method ?? '-'),
          subtitle: order.payment.providerRef == null
              ? null
              : Text(order.payment.providerRef!),
        ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.orderDetailTotals),
        _TotalsRow(
            label: l10n.cartTotalsSubtotal,
            value: money(order.totals.subtotal)),
        if ((double.tryParse(order.totals.discount) ?? 0) > 0)
          _TotalsRow(
              label: l10n.cartTotalsDiscount,
              value: '-${money(order.totals.discount)}'),
        _TotalsRow(label: l10n.cartTotalsTax, value: money(order.totals.tax)),
        if ((double.tryParse(order.totals.shipping) ?? 0) > 0)
          _TotalsRow(
              label: l10n.cartTotalsShipping,
              value: money(order.totals.shipping)),
        _TotalsRow(
            label: l10n.cartTotalsGrand,
            value: money(order.totals.grandTotal),
            emphasized: true),
      ],
    );
  }
}

class _ActionsToolbar extends StatelessWidget {
  const _ActionsToolbar({
    required this.order,
    required this.canReturn,
    required this.onCancel,
    required this.onReorder,
    required this.onReturn,
    required this.onRetry,
    required this.onTrack,
  });

  final OrderDetail order;
  final bool canReturn;
  final VoidCallback onCancel;
  final VoidCallback onReorder;
  final VoidCallback onReturn;
  final VoidCallback onRetry;
  final VoidCallback onTrack;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Wrap(
      spacing: AppSpacing.sm,
      runSpacing: AppSpacing.sm,
      children: [
        if (order.actions.canCancel)
          OutlinedButton.icon(
            icon: const Icon(Icons.cancel_outlined),
            label: Text(l10n.orderActionCancel),
            onPressed: onCancel,
          ),
        if (order.actions.canReorder)
          OutlinedButton.icon(
            icon: const Icon(Icons.replay),
            label: Text(l10n.orderActionReorder),
            onPressed: onReorder,
          ),
        if (canReturn)
          OutlinedButton.icon(
            icon: const Icon(Icons.assignment_return_outlined),
            label: Text(l10n.orderActionReturn),
            onPressed: onReturn,
          ),
        if (order.actions.canRetryPayment)
          FilledButton.icon(
            icon: const Icon(Icons.payment),
            label: Text(l10n.orderActionRetryPayment),
            onPressed: onRetry,
          ),
        if (order.shipment.tracking?.url != null)
          TextButton.icon(
            icon: const Icon(Icons.open_in_new),
            label: Text(l10n.orderActionTrack),
            onPressed: onTrack,
          ),
      ],
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Text(text, style: Theme.of(context).textTheme.titleSmall),
    );
  }
}

class _TotalsRow extends StatelessWidget {
  const _TotalsRow({
    required this.label,
    required this.value,
    this.emphasized = false,
  });
  final String label;
  final String value;
  final bool emphasized;

  @override
  Widget build(BuildContext context) {
    final style = emphasized
        ? Theme.of(context).textTheme.titleMedium
        : Theme.of(context).textTheme.bodyMedium;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(child: Text(label, style: style)),
          Text(value, style: style),
        ],
      ),
    );
  }
}
