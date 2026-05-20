import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/reorder_bloc.dart';
import '../data/models/order_models.dart';

class ReorderScreen extends StatelessWidget {
  const ReorderScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<ReorderBloc, ReorderState>(
      listener: (context, state) {
        if (state is ReorderDone) {
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(
            content: Text(l10n.orderReorderAddedToast(
                state.addedCount, state.skippedCount)),
          ));
          context.go('/cart');
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.orderReorderTitle)),
          body: switch (state) {
            ReorderLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ReorderConfirming() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            ReorderDone() => LoadingState(semanticsLabel: l10n.commonLoading),
            ReorderLoaded(:final result) => _Loaded(result: result, l10n: l10n),
            ReorderFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () =>
                    context.read<ReorderBloc>().add(const ReorderStarted()),
                retryLabel: l10n.commonRetry,
              ),
          },
        );
      },
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.result, required this.l10n});
  final ReorderResult result;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final hasAvailable = result.available.isNotEmpty;
    if (!hasAvailable && result.unavailable.isEmpty) {
      return EmptyState(title: l10n.orderReorderNothingTitle);
    }
    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.all(AppSpacing.md),
            children: [
              if (hasAvailable) ...[
                _SectionHeader(text: l10n.orderReorderAvailable),
                for (final line in result.available)
                  Builder(builder: (ctx) {
                    final qtyFmt = NumberFormat.decimalPattern(
                        Localizations.localeOf(ctx).toString());
                    return ListTile(
                      leading: const Icon(Icons.check_circle_outline,
                          color: Colors.green),
                      title: Text(line.name),
                      // Locale-aware quantity (e.g. `×٢` in AR) via
                      // `orderReorderQtyLabel`. NumberFormat handles
                      // Arabic-Indic digit shaping.
                      trailing: Text(l10n
                          .orderReorderQtyLabel(line.qty)
                          .replaceAll('${line.qty}', qtyFmt.format(line.qty))),
                    );
                  }),
              ],
              if (result.unavailable.isNotEmpty) ...[
                const SizedBox(height: AppSpacing.md),
                _SectionHeader(text: l10n.orderReorderUnavailable),
                for (final line in result.unavailable)
                  ListTile(
                    leading: const Icon(Icons.do_not_disturb_outlined,
                        color: Colors.red),
                    title: Text(line.name),
                    subtitle: Text(_reasonLabel(l10n, line.reason)),
                  ),
              ],
              if (!hasAvailable) ...[
                const SizedBox(height: AppSpacing.lg),
                Text(l10n.orderReorderNothingBody, textAlign: TextAlign.center),
              ],
            ],
          ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: FilledButton(
              onPressed: hasAvailable
                  ? () => context
                      .read<ReorderBloc>()
                      .add(const ReorderAddToCartConfirmed())
                  : null,
              child: Text(l10n.orderReorderAddToCart),
            ),
          ),
        ),
      ],
    );
  }

  String _reasonLabel(AppLocalizations l10n, String reason) {
    switch (reason) {
      case 'out_of_stock':
        return l10n.orderReorderReasonOutOfStock;
      case 'discontinued':
        return l10n.orderReorderReasonDiscontinued;
      case 'market_blocked':
        return l10n.orderReorderReasonMarketBlocked;
      default:
        // Unmapped reason from server — show the localized generic
        // fallback rather than raw English wire text.
        return l10n.orderReorderReasonUnknown;
    }
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Text(text, style: Theme.of(context).textTheme.titleSmall),
    );
  }
}
