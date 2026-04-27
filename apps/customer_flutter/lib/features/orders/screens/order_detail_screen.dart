import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/localization/locale_bloc.dart';
import '../../../generated/l10n/app_localizations.dart';
import '../../cart/widgets/cart_totals_panel.dart';
import '../bloc/order_detail_bloc.dart';
import '../data/order_view_models.dart';
import '../services/reorder_service.dart';
import '../widgets/order_timeline.dart';
import '../widgets/state_stream_chips.dart';
import '../widgets/tracking_link.dart';

class OrderDetailScreen extends StatelessWidget {
  const OrderDetailScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final localeCode = context.watch<LocaleBloc>().state.locale.code;
    return Scaffold(
      appBar: AppBar(),
      body: BlocBuilder<OrderDetailBloc, OrderDetailState>(
        builder: (context, state) {
          return switch (state) {
            OrderDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            OrderDetailError() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                onRetry: () => context
                    .read<OrderDetailBloc>()
                    .add(OrderDetailRequested(orderId)),
                retryLabel: l10n.commonRetry,
              ),
            OrderDetailLoaded(:final detail) =>
              _Body(detail: detail, localeCode: localeCode),
          };
        },
      ),
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.detail, required this.localeCode});
  final OrderDetailViewModel detail;
  final String localeCode;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return RefreshIndicator(
      onRefresh: () async =>
          context.read<OrderDetailBloc>().add(const OrderDetailRefreshed()),
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          Text(detail.orderNumber, style: AppTypography.headline),
          const SizedBox(height: AppSpacing.sm),
          StateStreamChips(
            orderState: detail.orderState,
            paymentState: detail.paymentState,
            fulfillmentState: detail.fulfillmentState,
            refundState: detail.refundState,
          ),
          const SizedBox(height: AppSpacing.lg),
          if (detail.tracking != null)
            TrackingLink(
              tracking: detail.tracking!,
              onOpen: (url) {
                // Universal link receiver in core/platform/app_links handles it.
              },
            ),
          const SizedBox(height: AppSpacing.md),
          OrderTimeline(events: detail.timeline, localeCode: localeCode),
          const SizedBox(height: AppSpacing.md),
          CartTotalsPanel(totals: detail.totals, localeCode: localeCode),
          const SizedBox(height: AppSpacing.lg),
          Row(
            children: [
              Expanded(
                child: AppButton(
                  label: l10n.commonContinue,
                  onPressed: () {
                    const ReorderService().plan(detail);
                    // Hook up cart-add via CartBloc in a follow-up.
                  },
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: AppButton(
                  label: l10n.commonContinue,
                  variant: AppButtonVariant.ghost,
                  onPressed: detail.refundEligibility.canRequest
                      ? () {
                          // Returns flow lives in spec 013; deferred.
                        }
                      : null,
                ),
              ),
            ],
          ),
          if (detail.invoiceDownloadUrl != null)
            Padding(
              padding: const EdgeInsets.only(top: AppSpacing.md),
              child: AppButton(
                label: l10n.commonContinue,
                variant: AppButtonVariant.secondary,
                expand: true,
                onPressed: () {
                  // Invoice download owned by spec 012; placeholder hook.
                },
              ),
            ),
        ],
      ),
    );
  }
}
