import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../../cart/data/cart_store.dart';
import '../data/models/checkout_models.dart';

/// S-4.10 confirmation. Clears the cart on entry (BR-1 default behavior),
/// shows the order number, and surfaces bank-transfer reference + IBAN
/// with copy CTAs when the submit response includes one (BR-7).
class OrderConfirmationScreen extends StatefulWidget {
  const OrderConfirmationScreen({
    super.key,
    required this.orderId,
    this.result,
    required this.cartStore,
  });

  final String orderId;
  final SubmitResult? result;
  final CartStore cartStore;

  @override
  State<OrderConfirmationScreen> createState() =>
      _OrderConfirmationScreenState();
}

class _OrderConfirmationScreenState extends State<OrderConfirmationScreen> {
  @override
  void initState() {
    super.initState();
    // Phase 4 BR-1: clear cart on submit-success. The Review screen also
    // calls clear() defensively; calling it here again is idempotent
    // (clear-of-empty is a no-op).
    WidgetsBinding.instance.addPostFrameCallback((_) {
      widget.cartStore.clear();
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final bt = widget.result?.bankTransfer;
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.orderConfirmationTitle)),
      body: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.check_circle, size: 80, color: AppColors.success),
            const SizedBox(height: AppSpacing.md),
            Text(l10n.orderConfirmationTitle,
                style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: AppSpacing.sm),
            Text(
              widget.result?.orderNumber ?? widget.orderId,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            if (bt != null) ...[
              const SizedBox(height: AppSpacing.lg),
              _BankTransferCard(details: bt),
            ],
            const SizedBox(height: AppSpacing.lg),
            FilledButton(
              onPressed: () => context.go('/o/${widget.orderId}'),
              child: Text(l10n.orderConfirmationViewOrder),
            ),
            const SizedBox(height: AppSpacing.sm),
            TextButton(
              onPressed: () => context.go('/'),
              child: Text(l10n.orderConfirmationContinue),
            ),
          ],
        ),
      ),
    );
  }
}

class _BankTransferCard extends StatelessWidget {
  const _BankTransferCard({required this.details});
  final BankTransferDetails details;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(l10n.orderConfirmationBankTitle,
                style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: AppSpacing.sm),
            _copyRow(context, 'Reference', details.reference),
            _copyRow(context, 'IBAN', details.iban),
            _copyRow(context, 'Amount', details.amount),
          ],
        ),
      ),
    );
  }

  Widget _copyRow(BuildContext context, String label, String value) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(child: Text('$label: $value')),
          TextButton.icon(
            icon: const Icon(Icons.copy, size: 16),
            label: Text(l10n.orderConfirmationBankCopy),
            onPressed: () => Clipboard.setData(ClipboardData(text: value)),
          ),
        ],
      ),
    );
  }
}
