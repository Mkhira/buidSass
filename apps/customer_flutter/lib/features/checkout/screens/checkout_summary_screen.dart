import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_summary_bloc.dart';
import '../data/models/checkout_models.dart';

class CheckoutSummaryScreen extends StatelessWidget {
  const CheckoutSummaryScreen({super.key, required this.sessionId});
  final String sessionId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.checkoutSummaryTitle)),
      body: BlocBuilder<CheckoutSummaryBloc, CheckoutSummaryState>(
        builder: (context, state) {
          return switch (state) {
            CheckoutSummaryLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutSummaryFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<CheckoutSummaryBloc>()
                    .add(const CheckoutSummaryRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            CheckoutSummaryLoaded(:final summary) => RefreshIndicator(
                onRefresh: () async => context
                    .read<CheckoutSummaryBloc>()
                    .add(const CheckoutSummaryRefreshed()),
                child: _LoadedBody(summary: summary),
              ),
          };
        },
      ),
    );
  }
}

class _LoadedBody extends StatelessWidget {
  const _LoadedBody({required this.summary});
  final CheckoutSummary summary;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: summary.totals.currency,
      decimalDigits: 2,
    );
    String money(String raw) => fmt.format(double.tryParse(raw) ?? 0);
    final addressDone =
        summary.stepStatus.address == CheckoutStepStatus.complete;
    final shippingDone =
        summary.stepStatus.shipping == CheckoutStepStatus.complete;
    final paymentDone =
        summary.stepStatus.payment == CheckoutStepStatus.complete;
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        _StepTile(
          label: l10n.checkoutStepAddress,
          subtitle: summary.address == null
              ? null
              : '${summary.address!.name} · ${summary.address!.street}',
          done: addressDone,
          enabled: true,
          onTap: () => context.push('/checkout/${summary.sessionId}/address'),
        ),
        _StepTile(
          label: l10n.checkoutStepShipping,
          subtitle: summary.shipping.method,
          done: shippingDone,
          enabled: addressDone,
          onTap: () => context.push('/checkout/${summary.sessionId}/shipping'),
        ),
        _StepTile(
          label: l10n.checkoutStepPayment,
          subtitle: summary.payment.method,
          done: paymentDone,
          enabled: shippingDone,
          onTap: () => context.push('/checkout/${summary.sessionId}/payment'),
        ),
        _StepTile(
          label: l10n.checkoutStepReview,
          subtitle: null,
          done: false,
          enabled: paymentDone,
          onTap: () => context.push('/checkout/${summary.sessionId}/review'),
        ),
        const Divider(),
        Padding(
          padding: const EdgeInsets.all(AppSpacing.sm),
          child: Row(
            children: [
              Expanded(child: Text(l10n.cartTotalsGrand)),
              Text(money(summary.totals.grandTotal),
                  style: Theme.of(context).textTheme.titleMedium),
            ],
          ),
        ),
      ],
    );
  }
}

class _StepTile extends StatelessWidget {
  const _StepTile({
    required this.label,
    required this.subtitle,
    required this.done,
    required this.enabled,
    required this.onTap,
  });
  final String label;
  final String? subtitle;
  final bool done;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ListTile(
        leading: Icon(done ? Icons.check_circle : Icons.radio_button_unchecked,
            color: done ? AppColors.success : null),
        title: Text(label),
        subtitle: subtitle == null ? null : Text(subtitle!),
        trailing: const Icon(Icons.chevron_right),
        enabled: enabled,
        onTap: enabled ? onTap : null,
      ),
    );
  }
}
