import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/legacy_quotation_detail_bloc.dart';

class LegacyQuotationDetailScreen extends StatelessWidget {
  const LegacyQuotationDetailScreen({super.key, required this.quotationId});
  final String quotationId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.legacyQuotationDetailTitle)),
      body: BlocBuilder<LegacyQuotationDetailBloc, LegacyQuotationDetailState>(
        builder: (context, state) {
          return switch (state) {
            LegacyQuotationDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            LegacyQuotationDetailLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<LegacyQuotationDetailBloc>()
                    .add(const LegacyQuotationDetailStarted()),
              ),
            LegacyQuotationDetailLoaded() => _Loaded(state: state),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final LegacyQuotationDetailLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    final q = state.quotation;
    return SafeArea(
      child: Column(
        children: [
          if (state.actionError != null)
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: AppColors.danger.withValues(alpha: 0.1),
                  border: Border.all(color: AppColors.danger),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  l10n.commonErrorBody,
                  style: const TextStyle(color: AppColors.danger),
                ),
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.all(AppSpacing.md),
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          q.quotationNumber,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: AppSpacing.xs),
                        Text(
                          dateFmt.format(q.createdAt.toLocal()),
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        if (q.validUntil != null) ...[
                          const SizedBox(height: AppSpacing.xs),
                          Text(
                            l10n.quotesListExpiresOn(
                              dateFmt.format(q.validUntil!.toLocal()),
                            ),
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                        const Divider(height: AppSpacing.lg),
                        for (final line in q.lines)
                          Padding(
                            padding:
                                const EdgeInsets.only(bottom: AppSpacing.xs),
                            child: Row(
                              children: [
                                Expanded(child: Text(line.name)),
                                Text('${line.qty} × ${line.unitPrice}'),
                                const SizedBox(width: AppSpacing.sm),
                                Text(
                                  '${line.lineTotal} ${q.currency}',
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        const Divider(height: AppSpacing.lg),
                        _total(context, l10n.quoteDetailSubtotal, q.subtotal,
                            q.currency),
                        _total(context, l10n.quoteDetailTax, q.tax, q.currency),
                        _total(
                          context,
                          l10n.quoteDetailGrandTotal,
                          q.grandTotal,
                          q.currency,
                          bold: true,
                        ),
                        if (q.terms != null && q.terms!.isNotEmpty) ...[
                          const Divider(height: AppSpacing.lg),
                          Text(
                            l10n.quoteDetailTermsLabel,
                            style: Theme.of(context).textTheme.titleSmall,
                          ),
                          const SizedBox(height: AppSpacing.xs),
                          Text(
                            q.terms!,
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
          if (q.canAccept || q.canReject)
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Row(
                children: [
                  if (q.canAccept)
                    Expanded(
                      child: AppButton(
                        label: l10n.legacyQuotationAcceptCta,
                        isLoading: state.busy,
                        onPressed:
                            state.busy ? null : () => _confirmAccept(context),
                      ),
                    ),
                  if (q.canAccept && q.canReject)
                    const SizedBox(width: AppSpacing.sm),
                  if (q.canReject)
                    Expanded(
                      child: OutlinedButton(
                        onPressed: state.busy
                            ? null
                            : () => context
                                .read<LegacyQuotationDetailBloc>()
                                .add(const LegacyQuotationRejected()),
                        style: OutlinedButton.styleFrom(
                          foregroundColor: AppColors.danger,
                          side: const BorderSide(color: AppColors.danger),
                        ),
                        child: Text(l10n.legacyQuotationRejectCta),
                      ),
                    ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  Future<void> _confirmAccept(BuildContext context) async {
    final l10n = AppLocalizations.of(context);
    final result = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(l10n.legacyQuotationAcceptConfirmTitle),
        content: Text(l10n.legacyQuotationAcceptConfirmBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: Text(l10n.commonCancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: Text(l10n.legacyQuotationAcceptCta),
          ),
        ],
      ),
    );
    if (result == true && context.mounted) {
      context
          .read<LegacyQuotationDetailBloc>()
          .add(const LegacyQuotationAccepted());
    }
  }

  Widget _total(
    BuildContext context,
    String label,
    String amount,
    String currency, {
    bool bold = false,
  }) {
    final style = Theme.of(context).textTheme.bodyMedium?.copyWith(
          fontWeight: bold ? FontWeight.w700 : FontWeight.normal,
        );
    return Padding(
      padding: const EdgeInsets.only(bottom: 2),
      child: Row(
        children: [
          Expanded(child: Text(label, style: style)),
          Text('$amount $currency', style: style),
        ],
      ),
    );
  }
}
