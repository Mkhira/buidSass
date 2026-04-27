import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_bloc.dart';
import '../data/checkout_view_models.dart';
import '../widgets/address_picker.dart';
import '../widgets/payment_method_picker.dart';
import '../widgets/shipping_quote_picker.dart';

class CheckoutScreen extends StatelessWidget {
  const CheckoutScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l10n.checkoutSubmit)),
      body: BlocConsumer<CheckoutBloc, CheckoutState>(
        listener: (context, state) {
          // CheckoutDriftBlocked is rendered inline (the bloc owns the
          // drift details, and navigating away would lose state).
          // Only navigate on terminal outcomes.
          if (state is CheckoutSubmitted) {
            context.go('/checkout/confirmation/${state.outcome.orderId}');
          } else if (state is CheckoutFailedTerminal) {
            context.go('/cart');
          }
        },
        builder: (context, state) {
          return switch (state) {
            CheckoutIdle() => Center(
                child: AppButton(
                  label: l10n.commonContinue,
                  onPressed: () => context
                      .read<CheckoutBloc>()
                      .add(const CheckoutStarted()),
                ),
              ),
            CheckoutDrafting(
              :final session,
              :final selectedAddressId,
              :final selectedQuoteId,
              :final selectedPaymentMethodId,
              :final transientError,
            ) =>
              _Stepper(
                session: session,
                addressId: selectedAddressId,
                quoteId: selectedQuoteId,
                paymentId: selectedPaymentMethodId,
                submitEnabled: false,
                transientError: transientError,
              ),
            CheckoutReady(
              :final session,
              :final selectedAddressId,
              :final selectedQuoteId,
              :final selectedPaymentMethodId,
            ) =>
              _Stepper(
                session: session,
                addressId: selectedAddressId,
                quoteId: selectedQuoteId,
                paymentId: selectedPaymentMethodId,
                submitEnabled: true,
              ),
            CheckoutSubmitting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutDriftBlocked(:final details) => _DriftInline(details: details),
            CheckoutFailed(:final reasonCode) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reasonCode,
                onRetry: () =>
                    context.read<CheckoutBloc>().add(const RetryTapped()),
                retryLabel: l10n.commonRetry,
              ),
            CheckoutSubmitted() ||
            CheckoutFailedTerminal() =>
              const SizedBox.shrink(),
          };
        },
      ),
    );
  }
}

class _Stepper extends StatelessWidget {
  const _Stepper({
    required this.session,
    required this.addressId,
    required this.quoteId,
    required this.paymentId,
    required this.submitEnabled,
    this.transientError,
  });

  final CheckoutSession session;
  final String? addressId;
  final String? quoteId;
  final String? paymentId;
  final bool submitEnabled;
  final String? transientError;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      children: [
        if (transientError != null)
          Container(
            margin: const EdgeInsets.all(AppSpacing.md),
            padding: const EdgeInsets.all(AppSpacing.sm),
            decoration: BoxDecoration(
              color: AppColors.danger.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: AppColors.danger),
            ),
            child: Text(
              l10n.commonErrorBody,
              style: const TextStyle(color: AppColors.danger),
            ),
          ),
        const SizedBox(height: AppSpacing.md),
        AddressPicker(
          addresses: session.availableAddresses,
          selectedId: addressId,
          onSelected: (id) =>
              context.read<CheckoutBloc>().add(AddressSelected(id)),
        ),
        const Divider(),
        ShippingQuotePicker(
          quotes: session.availableQuotes,
          selectedId: quoteId,
          onSelected: (id) =>
              context.read<CheckoutBloc>().add(ShippingSelected(id)),
        ),
        const Divider(),
        PaymentMethodPicker(
          methods: session.availablePaymentMethods,
          selectedId: paymentId,
          onSelected: (id) =>
              context.read<CheckoutBloc>().add(PaymentSelected(id)),
        ),
        const SizedBox(height: AppSpacing.lg),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
          child: AppButton(
            label: l10n.checkoutSubmit,
            expand: true,
            onPressed: submitEnabled
                ? () => context.read<CheckoutBloc>().add(const SubmitTapped())
                : null,
          ),
        ),
      ],
    );
  }
}

class _DriftInline extends StatelessWidget {
  const _DriftInline({required this.details});
  final CheckoutDriftDetails details;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        children: [
          Text(l10n.commonErrorTitle, style: AppTypography.headline),
          const SizedBox(height: AppSpacing.md),
          Text(l10n.commonErrorBody),
          const SizedBox(height: AppSpacing.sm),
          Text('${details.changedLines.length} line(s) updated'),
          const SizedBox(height: AppSpacing.md),
          AppButton(
            label: l10n.commonContinue,
            expand: true,
            onPressed: () =>
                context.read<CheckoutBloc>().add(const DriftAccepted()),
          ),
        ],
      ),
    );
  }
}
