import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_drift.dart';
import '../bloc/checkout_shipping_bloc.dart';
import '../widgets/conflict_dialog.dart';

class ShippingStepScreen extends StatelessWidget {
  const ShippingStepScreen({super.key, required this.sessionId});
  final String sessionId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CheckoutShippingBloc, CheckoutShippingState>(
      listener: (context, state) async {
        if (state is CheckoutShippingSubmitted) {
          await context.push('/checkout/$sessionId/payment');
        } else if (state is CheckoutShippingConflict) {
          final r = await showConflictDialog(context, state.conflict);
          if (!context.mounted) return;
          if (r == DriftResolution.review) {
            context.go('/checkout/$sessionId/summary');
          }
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.checkoutShippingTitle)),
          body: switch (state) {
            CheckoutShippingLoadingQuotes() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutShippingEmpty() => EmptyState(
                title: l10n.checkoutShippingEmpty,
                action: FilledButton(
                  onPressed: () => context.go('/checkout/$sessionId/address'),
                  child: Text(l10n.commonRetry),
                ),
              ),
            CheckoutShippingLoaded() => _OptionsList(state: state),
            CheckoutShippingSubmitting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutShippingSubmitted() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutShippingConflict() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutShippingFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<CheckoutShippingBloc>()
                    .add(const ShippingQuotesRequested()),
                retryLabel: l10n.commonRetry,
              ),
          },
        );
      },
    );
  }
}

class _OptionsList extends StatelessWidget {
  const _OptionsList({required this.state});
  final CheckoutShippingLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    return Column(
      children: [
        Expanded(
          child: ListView.builder(
            itemCount: state.options.length,
            itemBuilder: (context, i) {
              final o = state.options[i];
              final fmt = NumberFormat.currency(
                locale: locale,
                symbol: o.price.currency,
                decimalDigits: 2,
              );
              final selected = state.selectedMethod == o.method;
              return ListTile(
                leading: Icon(
                  selected
                      ? Icons.radio_button_checked
                      : Icons.radio_button_unchecked,
                ),
                title: Text(o.label),
                subtitle: Text(
                    '${fmt.format(double.tryParse(o.price.amount) ?? 0)} · ${o.etaDays}'),
                onTap: () => context
                    .read<CheckoutShippingBloc>()
                    .add(ShippingMethodSelected(o.method)),
              );
            },
          ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: FilledButton(
              onPressed: state.selectedMethod == null
                  ? null
                  : () => context
                      .read<CheckoutShippingBloc>()
                      .add(ShippingSubmitted(state.selectedMethod!)),
              child: Text(l10n.checkoutContinue),
            ),
          ),
        ),
      ],
    );
  }
}
