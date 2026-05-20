import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../../checkout/data/models/checkout_models.dart';
import '../bloc/cart_v2_bloc.dart';
import '../data/models/cart_store_models.dart';

/// Phase 4 S-4.1 cart. Reads/writes `CartStore` via [CartV2Bloc] and
/// renders totals from the `price-cart` preview (BR-2). All money values
/// arrive as server-formatted decimal strings and render through
/// `NumberFormat.currency` to keep locale + RTL formatting correct.
class CartScreenV2 extends StatelessWidget {
  const CartScreenV2({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CartV2Bloc, CartV2State>(
      listener: (context, state) {
        if (state is CartV2Proceeding) {
          context.go('/checkout/${state.sessionId}/summary');
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.navCart)),
          body: switch (state) {
            CartV2Loading() => LoadingState(semanticsLabel: l10n.commonLoading),
            CartV2Empty() => EmptyState(title: l10n.cartEmpty),
            CartV2Loaded() => _LoadedBody(state: state),
            CartV2Failure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () =>
                    context.read<CartV2Bloc>().add(const CartRefreshedV2()),
                retryLabel: l10n.commonRetry,
              ),
            CartV2Proceeding() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
          },
        );
      },
    );
  }
}

class _LoadedBody extends StatelessWidget {
  const _LoadedBody({required this.state});
  final CartV2Loaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Column(
      children: [
        Expanded(
          child: ListView.separated(
            padding: const EdgeInsets.all(AppSpacing.md),
            itemCount: state.snapshot.lines.length,
            separatorBuilder: (_, __) => const SizedBox(height: AppSpacing.sm),
            itemBuilder: (context, i) {
              final line = state.snapshot.lines[i];
              final unavailable =
                  state.unavailableProductIds.contains(line.productId);
              return _CartLineTile(
                line: line,
                unavailable: unavailable,
              );
            },
          ),
        ),
        const Divider(height: 1),
        Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: _CouponInput(
            currentCoupon: state.snapshot.couponCode,
            error: state.couponError,
          ),
        ),
        _TotalsPanel(totals: state.totals, isLoading: state.isQuoteInFlight),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: FilledButton(
              onPressed: state.isQuoteInFlight || state.hasUnavailable
                  ? null
                  : () => context
                      .read<CartV2Bloc>()
                      .add(const CartProceedRequested()),
              child: Text(l10n.cartProceed),
            ),
          ),
        ),
      ],
    );
  }
}

class _CartLineTile extends StatelessWidget {
  const _CartLineTile({required this.line, required this.unavailable});
  final CartStoreLine line;
  final bool unavailable;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: line.currency,
      decimalDigits: 2,
    );
    final unitPrice = fmt.format(line.unitPriceMinor / 100);
    final textStyle = Theme.of(context).textTheme.bodyMedium?.copyWith(
          decoration: unavailable ? TextDecoration.lineThrough : null,
        );
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.sm),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 64,
              height: 64,
              child: ColoredBox(
                color: AppColors.neutral,
                child: line.imageUrl.isEmpty
                    ? const Icon(Icons.image_outlined)
                    : Image.network(line.imageUrl, fit: BoxFit.cover),
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(line.name, style: textStyle),
                  Text(unitPrice, style: textStyle),
                  if (unavailable)
                    Text(
                      l10n.cartLineUnavailable,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                            color: Theme.of(context).colorScheme.error,
                          ),
                    ),
                ],
              ),
            ),
            if (unavailable)
              TextButton(
                onPressed: () => context
                    .read<CartV2Bloc>()
                    .add(CartLineRemoved(line.productId)),
                child: Text(l10n.cartLineRemove),
              )
            else
              _QtyStepper(line: line),
          ],
        ),
      ),
    );
  }
}

class _QtyStepper extends StatelessWidget {
  const _QtyStepper({required this.line});
  final CartStoreLine line;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        IconButton(
          icon: const Icon(Icons.remove),
          onPressed: () => context.read<CartV2Bloc>().add(
                CartLineQtyChanged(
                    productId: line.productId, qty: line.qty - 1),
              ),
        ),
        Text('${line.qty}'),
        IconButton(
          icon: const Icon(Icons.add),
          onPressed: () => context.read<CartV2Bloc>().add(
                CartLineQtyChanged(
                    productId: line.productId, qty: line.qty + 1),
              ),
        ),
      ],
    );
  }
}

class _CouponInput extends StatefulWidget {
  const _CouponInput({required this.currentCoupon, required this.error});
  final String? currentCoupon;
  final String? error;

  @override
  State<_CouponInput> createState() => _CouponInputState();
}

class _CouponInputState extends State<_CouponInput> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(text: widget.currentCoupon ?? '');
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: TextField(
            controller: _controller,
            decoration: InputDecoration(
              labelText: l10n.cartCouponLabel,
              errorText: widget.error,
              border: const OutlineInputBorder(),
            ),
            onSubmitted: (v) =>
                context.read<CartV2Bloc>().add(CartCouponApplied(v.trim())),
          ),
        ),
        const SizedBox(width: AppSpacing.sm),
        if (widget.currentCoupon == null)
          FilledButton(
            onPressed: () => context
                .read<CartV2Bloc>()
                .add(CartCouponApplied(_controller.text.trim())),
            child: Text(l10n.cartCouponApply),
          )
        else
          OutlinedButton(
            onPressed: () {
              _controller.clear();
              context.read<CartV2Bloc>().add(const CartCouponCleared());
            },
            child: Text(l10n.cartCouponClear),
          ),
      ],
    );
  }
}

class _TotalsPanel extends StatelessWidget {
  const _TotalsPanel({required this.totals, required this.isLoading});
  final CheckoutTotals totals;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: totals.currency,
      decimalDigits: 2,
    );
    String money(String raw) => fmt.format(double.tryParse(raw) ?? 0);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        children: [
          _row(l10n.cartTotalsSubtotal, money(totals.subtotal)),
          if ((double.tryParse(totals.discount) ?? 0) > 0)
            _row(l10n.cartTotalsDiscount, '-${money(totals.discount)}'),
          _row(l10n.cartTotalsTax, money(totals.tax)),
          if ((double.tryParse(totals.shipping) ?? 0) > 0)
            _row(l10n.cartTotalsShipping, money(totals.shipping)),
          const Divider(),
          Row(
            children: [
              Expanded(
                child: Text(l10n.cartTotalsGrand,
                    style: Theme.of(context).textTheme.titleMedium),
              ),
              if (isLoading)
                const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              else
                Text(money(totals.grandTotal),
                    style: Theme.of(context).textTheme.titleMedium),
            ],
          ),
        ],
      ),
    );
  }

  Widget _row(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(child: Text(label)),
          Text(value),
        ],
      ),
    );
  }
}
