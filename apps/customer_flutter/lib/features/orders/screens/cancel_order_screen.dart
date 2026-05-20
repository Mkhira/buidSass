import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/cancel_order_bloc.dart';
import '../data/models/order_models.dart';

class CancelOrderScreen extends StatelessWidget {
  const CancelOrderScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return BlocConsumer<CancelOrderBloc, CancelOrderState>(
      listener: (context, state) {
        if (state is CancelOrderSuccess) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text(l10n.orderCancelSuccessToast)),
          );
          context.go('/o/$orderId');
        }
      },
      builder: (context, state) {
        return AppScaffold(
          appBar: AppBar(title: Text(l10n.orderCancelTitle)),
          body: switch (state) {
            CancelOrderForm() => _Body(state: state),
            CancelOrderSubmitting() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CancelOrderSuccess() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            CancelOrderStaleConflict() => _StaleConflict(orderId: orderId),
            CancelOrderFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context.pop(),
                retryLabel: l10n.commonRetry,
              ),
          },
        );
      },
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.state});
  final CancelOrderForm state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          DropdownButtonFormField<String>(
            initialValue: state.reason,
            decoration: InputDecoration(labelText: l10n.orderCancelReasonLabel),
            items: [
              for (final r in kCancelReasonFallback)
                DropdownMenuItem(
                    value: r.code, child: Text(_reasonLabel(l10n, r.code))),
            ],
            onChanged: (v) {
              if (v == null) return;
              context.read<CancelOrderBloc>().add(CancelReasonChanged(v));
            },
          ),
          const SizedBox(height: AppSpacing.md),
          TextField(
            maxLines: 3,
            decoration: InputDecoration(
              labelText: l10n.orderCancelNoteLabel,
              border: const OutlineInputBorder(),
            ),
            onChanged: (v) =>
                context.read<CancelOrderBloc>().add(CancelNoteChanged(v)),
          ),
          const SizedBox(height: AppSpacing.lg),
          FilledButton(
            onPressed: state.reason == null
                ? null
                : () => context
                    .read<CancelOrderBloc>()
                    .add(const CancelSubmitted()),
            child: Text(l10n.orderCancelSubmit),
          ),
        ],
      ),
    );
  }
}

/// Map fallback reason codes to localized labels. The fallback list in
/// `order_models.dart` keeps English copy as dev-side defaults; the
/// screen layer is responsible for showing localized text per
/// Principle 4.
String _reasonLabel(AppLocalizations l10n, String code) {
  switch (code) {
    case 'changed_mind':
      return l10n.orderCancelReasonChangedMind;
    case 'ordered_wrong_item':
      return l10n.orderCancelReasonWrongItem;
    case 'delivery_too_slow':
      return l10n.orderCancelReasonDeliverySlow;
    case 'found_better_price':
      return l10n.orderCancelReasonBetterPrice;
    case 'other':
      return l10n.orderCancelReasonOther;
    default:
      return code;
  }
}

class _StaleConflict extends StatelessWidget {
  const _StaleConflict({required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(Icons.warning_amber_outlined,
              size: 56, color: Colors.orange),
          const SizedBox(height: AppSpacing.md),
          Text(l10n.orderCancelStaleBanner, textAlign: TextAlign.center),
          const SizedBox(height: AppSpacing.lg),
          FilledButton(
            onPressed: () => context.go('/o/$orderId'),
            child: Text(l10n.commonRetry),
          ),
        ],
      ),
    );
  }
}
