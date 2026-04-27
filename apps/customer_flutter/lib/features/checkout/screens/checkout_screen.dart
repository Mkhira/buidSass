import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/checkout_bloc.dart';
import '../widgets/address_picker.dart';
import '../widgets/payment_method_picker.dart';
import '../widgets/shipping_quote_picker.dart';

class CheckoutScreen extends StatelessWidget {
  const CheckoutScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(),
      body: BlocConsumer<CheckoutBloc, CheckoutState>(
        listener: (context, state) {
          if (state is CheckoutSubmitted) {
            context.go('/checkout/confirmation/${state.outcome.orderId}');
          } else if (state is CheckoutDriftBlocked) {
            context.go('/checkout/drift');
          } else if (state is CheckoutFailedTerminal) {
            context.go('/cart');
          }
        },
        builder: (context, state) {
          return switch (state) {
            CheckoutIdle() => Center(
                child: AppButton(
                  label: l10n.commonContinue,
                  onPressed: () =>
                      context.read<CheckoutBloc>().add(const CheckoutStarted()),
                ),
              ),
            CheckoutDrafting(:final session, :final selectedAddressId, :final selectedQuoteId, :final selectedPaymentMethodId) =>
              _Stepper(
                session: session,
                addressId: selectedAddressId,
                quoteId: selectedQuoteId,
                paymentId: selectedPaymentMethodId,
                submitEnabled: false,
              ),
            CheckoutReady(:final session, :final selectedAddressId, :final selectedQuoteId, :final selectedPaymentMethodId) =>
              _Stepper(
                session: session,
                addressId: selectedAddressId,
                quoteId: selectedQuoteId,
                paymentId: selectedPaymentMethodId,
                submitEnabled: true,
              ),
            CheckoutSubmitting() => LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutFailed(:final reasonCode) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reasonCode,
                onRetry: () => context.read<CheckoutBloc>().add(const RetryTapped()),
                retryLabel: l10n.commonRetry,
              ),
            CheckoutSubmitted() ||
            CheckoutDriftBlocked() ||
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
  });

  final dynamic session;
  final String? addressId;
  final String? quoteId;
  final String? paymentId;
  final bool submitEnabled;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return ListView(
      children: [
        const SizedBox(height: AppSpacing.md),
        AddressPicker(
          addresses: List.castFrom(session.availableAddresses),
          selectedId: addressId,
          onSelected: (id) =>
              context.read<CheckoutBloc>().add(AddressSelected(id)),
        ),
        const Divider(),
        ShippingQuotePicker(
          quotes: List.castFrom(session.availableQuotes),
          selectedId: quoteId,
          onSelected: (id) =>
              context.read<CheckoutBloc>().add(ShippingSelected(id)),
        ),
        const Divider(),
        PaymentMethodPicker(
          methods: List.castFrom(session.availablePaymentMethods),
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
