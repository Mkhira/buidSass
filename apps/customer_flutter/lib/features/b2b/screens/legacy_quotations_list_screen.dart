import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/legacy_quotations_list_bloc.dart';
import '../data/models/legacy_quotation_models.dart';

class LegacyQuotationsListScreen extends StatelessWidget {
  const LegacyQuotationsListScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.legacyQuotationsTitle)),
      body: BlocBuilder<LegacyQuotationsListBloc, LegacyQuotationsListState>(
        builder: (context, state) {
          return switch (state) {
            LegacyQuotationsListLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            LegacyQuotationsListEmpty() => EmptyState(
                title: l10n.legacyQuotationsEmpty,
                body: l10n.legacyQuotationsEmptyBody,
                icon: Icons.history_outlined,
              ),
            LegacyQuotationsListLoaded() => RefreshIndicator(
                onRefresh: () async {
                  final bloc = context.read<LegacyQuotationsListBloc>();
                  bloc.add(const LegacyQuotationsListRefreshed());
                  await bloc.stream
                      .firstWhere((s) => s is! LegacyQuotationsListLoading)
                      .timeout(
                        const Duration(seconds: 10),
                        onTimeout: () => bloc.state,
                      );
                },
                child: ListView.builder(
                  padding: const EdgeInsets.all(AppSpacing.md),
                  itemCount: state.items.length,
                  itemBuilder: (context, i) => _Row(item: state.items[i]),
                ),
              ),
            LegacyQuotationsListFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<LegacyQuotationsListBloc>()
                    .add(const LegacyQuotationsListRefreshed()),
              ),
          };
        },
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.item});
  final LegacyQuotationListItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    return Card(
      child: InkWell(
        onTap: () => context.push('/legacy-quotations/${item.id}'),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.md),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      item.quotationNumber,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  ),
                  Text(
                    _stateLabel(l10n, item.state),
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: AppColors.textSecondary,
                        ),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                dateFmt.format(item.createdAt.toLocal()),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (item.totalAmount != null && item.totalCurrency != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  '${item.totalAmount} ${item.totalCurrency}',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  String _stateLabel(AppLocalizations l10n, String state) {
    return switch (state) {
      'pending' => l10n.legacyQuotationStatePending,
      'accepted' => l10n.legacyQuotationStateAccepted,
      'rejected' => l10n.legacyQuotationStateRejected,
      'expired' => l10n.legacyQuotationStateExpired,
      _ => state,
    };
  }
}
