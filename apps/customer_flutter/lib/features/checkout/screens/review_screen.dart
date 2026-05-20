import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../../cart/data/cart_store.dart';
import '../bloc/checkout_drift.dart';
import '../bloc/checkout_review_bloc.dart';
import '../data/models/checkout_models.dart';
import '../widgets/conflict_dialog.dart';

class ReviewScreen extends StatelessWidget {
  const ReviewScreen({super.key, required this.sessionId});
  final String sessionId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CheckoutReviewBloc, CheckoutReviewState>(
      listener: (context, state) async {
        if (state is CheckoutReviewSuccess) {
          // Cart-clear is owned by the confirmation screen (T-4.15) but
          // we cancel it here as a defensive belt + suspenders: if the
          // user backs out of confirmation before it mounts we still
          // want a fresh cart on next entry.
          await context.read<CartStore>().clear();
          if (!context.mounted) return;
          context.go(
            '/checkout/confirmation/${state.result.orderId}',
            extra: state.result,
          );
        } else if (state is CheckoutReviewRedirecting) {
          // For Phase 4 V1 the WebView return handler is stubbed — we
          // bypass the redirect and emit success immediately. Real
          // 3DS/provider WebView handoff lands when SDK creds arrive
          // (see redirect_webview.dart placeholder).
          context
              .read<CheckoutReviewBloc>()
              .add(const ReviewRedirectReturned(success: true));
        } else if (state is CheckoutReviewConflict) {
          final r = await showConflictDialog(context, state.conflict);
          if (!context.mounted) return;
          if (r == DriftResolution.accept) {
            context.read<CheckoutReviewBloc>().add(const ReviewDriftAccepted());
          } else if (r == DriftResolution.review) {
            context.go('/checkout/$sessionId/summary');
          }
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.checkoutReviewTitle)),
          body: switch (state) {
            CheckoutReviewLoaded(:final summary) => _Body(summary: summary),
            CheckoutReviewSubmitting() ||
            CheckoutReviewRedirecting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutReviewSuccess() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutReviewConflict() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CheckoutReviewFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<CheckoutReviewBloc>()
                    .add(const ReviewSubmitted()),
                retryLabel: l10n.commonRetry,
              ),
          },
        );
      },
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.summary});
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
    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.all(AppSpacing.md),
            children: [
              for (final line in summary.lines)
                ListTile(
                  title: Text(line.name),
                  trailing: Text('${line.qty} · ${money(line.lineTotal)}'),
                ),
              const Divider(),
              if (summary.address != null)
                ListTile(
                  leading: const Icon(Icons.location_on_outlined),
                  title: Text(summary.address!.name),
                  subtitle: Text(
                      '${summary.address!.street}, ${summary.address!.city}'),
                ),
              if (summary.shipping.method != null)
                ListTile(
                  leading: const Icon(Icons.local_shipping_outlined),
                  title: Text(summary.shipping.method!),
                  trailing: summary.shipping.cost == null
                      ? null
                      : Text(money(summary.shipping.cost!.amount)),
                ),
              if (summary.payment.method != null)
                ListTile(
                  leading: const Icon(Icons.payment_outlined),
                  title: Text(summary.payment.method!),
                ),
              const Divider(),
              ListTile(
                title: Text(l10n.cartTotalsGrand),
                trailing: Text(money(summary.totals.grandTotal),
                    style: Theme.of(context).textTheme.titleMedium),
              ),
            ],
          ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: FilledButton(
              onPressed: () => context
                  .read<CheckoutReviewBloc>()
                  .add(const ReviewSubmitted()),
              child: Text(l10n.checkoutPlaceOrder),
            ),
          ),
        ),
      ],
    );
  }
}
