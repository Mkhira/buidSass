import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../core/localization/locale_bloc.dart';
import '../../../generated/l10n/app_localizations.dart';
import '../bloc/cart_bloc.dart';
import '../widgets/cart_line_tile.dart';
import '../widgets/cart_out_of_sync_banner.dart';
import '../widgets/cart_totals_panel.dart';

class CartScreen extends StatelessWidget {
  const CartScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final localeCode = context.watch<LocaleBloc>().state.locale.code;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.navCart)),
      body: BlocBuilder<CartBloc, CartState>(
        builder: (context, state) {
          return switch (state) {
            CartLoading() => LoadingState(semanticsLabel: l10n.commonLoading),
            CartEmpty() => EmptyState(title: l10n.cartEmpty),
            CartError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context.read<CartBloc>().add(const CartRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            CartLoaded() ||
            CartMutating() ||
            CartOutOfSync() =>
              _Body(state: state, localeCode: localeCode),
          };
        },
      ),
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.state, required this.localeCode});
  final CartState state;
  final String localeCode;

  CartLoaded get _loaded => switch (state) {
        CartLoaded() => state as CartLoaded,
        CartMutating(:final previous) => previous,
        CartOutOfSync(:final previous) => previous,
        _ => throw StateError('unsupported state'),
      };

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final loaded = _loaded;
    final blockCheckout = loaded.cart.verificationRevokedLines.isNotEmpty;
    return Column(
      children: [
        if (state is CartOutOfSync)
          CartOutOfSyncBanner(
            message: l10n.commonErrorBody,
            onReload: () =>
                context.read<CartBloc>().add(const CartRefreshed()),
          ),
        if (blockCheckout)
          CartOutOfSyncBanner(message: l10n.verificationRequired),
        Expanded(
          child: ListView.builder(
            itemCount: loaded.cart.lines.length,
            itemBuilder: (ctx, i) {
              final line = loaded.cart.lines[i];
              return CartLineTile(
                line: line,
                onQuantity: (q) => context
                    .read<CartBloc>()
                    .add(LineQuantityChanged(
                      productId: line.productId,
                      quantity: q,
                    )),
                onRemove: () => context
                    .read<CartBloc>()
                    .add(LineRemoved(line.productId)),
              );
            },
          ),
        ),
        CartTotalsPanel(totals: loaded.cart.totals, localeCode: localeCode),
        SafeArea(
          minimum: const EdgeInsets.all(AppSpacing.md),
          child: AppButton(
            label: l10n.cartProceedToCheckout,
            expand: true,
            onPressed: blockCheckout ? null : () => context.go('/checkout'),
          ),
        ),
      ],
    );
  }
}
