import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_drift.dart';
import '../bloc/checkout_payment_bloc.dart';
import '../payment_adapters/payment_adapter.dart';
import '../widgets/conflict_dialog.dart';

class PaymentStepScreen extends StatelessWidget {
  const PaymentStepScreen({super.key, required this.sessionId});
  final String sessionId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CheckoutPaymentBloc, CheckoutPaymentState>(
      listener: (context, state) async {
        if (state is CheckoutPaymentSubmitted) {
          await context.push('/checkout/$sessionId/review');
        } else if (state is CheckoutPaymentConflict) {
          final r = await showConflictDialog(context, state.conflict);
          if (!context.mounted) return;
          if (r == DriftResolution.accept) {
            // Drift on payment must round-trip through summary so the
            // updated `availableMethods` list and totals are visible
            // before the user re-selects a method.
            context.go('/checkout/$sessionId/summary');
          } else if (r == DriftResolution.review) {
            context.go('/checkout/$sessionId/summary');
          }
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.checkoutPaymentTitle)),
          body: switch (state) {
            CheckoutPaymentIdle(:final summary) =>
              _MethodPicker(summary: summary),
            CheckoutPaymentSubmitting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutPaymentCollecting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutPaymentSubmitted() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutPaymentConflict() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutPaymentFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => Navigator.of(context).pop(),
                retryLabel: l10n.commonRetry,
              ),
          },
        );
      },
    );
  }
}

class _MethodPicker extends StatelessWidget {
  const _MethodPicker({required this.summary});
  final dynamic summary;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final methods = summary.availableMethods as List<String>;
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        for (final m in methods)
          Card(
            child: ListTile(
              title: Text(_label(l10n, m)),
              trailing: const Icon(Icons.chevron_right),
              onTap: () async {
                final bloc = context.read<CheckoutPaymentBloc>();
                final adapter = bloc.adapterFor(m);
                if (adapter == null) return;
                // Adapter calls can throw — provider SDKs surface
                // network / cancel errors as exceptions, and we don't
                // want those to escape to the UI thread once real
                // adapters ship. Treat any throw as cancellation; the
                // user can retry from the picker.
                PaymentTokenResult result;
                try {
                  result = await adapter.collectToken(
                    summary: summary,
                    context: context,
                  );
                } on Object {
                  result = PaymentTokenResult(method: m, cancelled: true);
                }
                if (!context.mounted) return;
                bloc.add(PaymentMethodChosen(method: m, token: result));
              },
            ),
          ),
      ],
    );
  }

  String _label(AppLocalizations l10n, String method) {
    switch (method) {
      case 'card':
        return l10n.paymentMethodCard;
      case 'apple_pay':
        return l10n.paymentMethodApplePay;
      case 'mada':
        return l10n.paymentMethodMada;
      case 'stc_pay':
        return l10n.paymentMethodStcPay;
      case 'tabby':
        return l10n.paymentMethodTabby;
      case 'tamara':
        return l10n.paymentMethodTamara;
      case 'valu':
        return l10n.paymentMethodValu;
      case 'meeza':
        return l10n.paymentMethodMeeza;
      case 'bank_transfer':
        return l10n.paymentMethodBankTransfer;
      case 'cod':
        return l10n.paymentMethodCod;
      default:
        return method;
    }
  }
}
