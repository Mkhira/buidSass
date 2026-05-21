import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/quote_detail_bloc.dart';
import '../data/models/quote_models.dart';
import '../widgets/quote_actions_toolbar.dart';
import '../widgets/quote_state_pill.dart';
import '../widgets/quote_version_timeline.dart';

/// S-8.5 — quote detail with the 6 transition actions.
class QuoteDetailScreen extends StatelessWidget {
  const QuoteDetailScreen({super.key, required this.quoteId});
  final String quoteId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.quoteDetailTitle)),
      body: BlocBuilder<QuoteDetailBloc, QuoteDetailState>(
        builder: (context, state) {
          return switch (state) {
            QuoteDetailLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            QuoteDetailLoadFailure() => ErrorState(
                title: l10n.commonErrorTitle,
                body: l10n.commonErrorBody,
                retryLabel: l10n.commonRetry,
                onRetry: () => context
                    .read<QuoteDetailBloc>()
                    .add(const QuoteDetailStarted()),
              ),
            QuoteDetailLoaded() => _Loaded(state: state),
          };
        },
      ),
    );
  }
}

class _Loaded extends StatelessWidget {
  const _Loaded({required this.state});
  final QuoteDetailLoaded state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final dateFmt = DateFormat.yMMMd(locale);
    final quote = state.quote;
    final latest = quote.latestVersion;
    return RefreshIndicator(
      onRefresh: () async {
        final bloc = context.read<QuoteDetailBloc>();
        bloc.add(const QuoteDetailRefreshed());
        await bloc.stream.firstWhere((s) => s is QuoteDetailLoaded).timeout(
              const Duration(seconds: 10),
              onTimeout: () => bloc.state,
            );
      },
      child: ListView(
        padding: const EdgeInsets.all(AppSpacing.md),
        children: [
          if (state.actionError != null)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.md),
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
          Card(
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          quote.quoteNumber,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      QuoteStatePill(state: quote.state),
                    ],
                  ),
                  if (quote.submittedByName != null) ...[
                    const SizedBox(height: AppSpacing.xs),
                    Text(
                      l10n.quoteSubmittedBy(quote.submittedByName!),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ],
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: QuoteVersionTimeline(versions: quote.versions),
            ),
          ),
          if (latest != null) ...[
            const SizedBox(height: AppSpacing.md),
            _LatestVersionCard(version: latest, dateFmt: dateFmt),
            const SizedBox(height: AppSpacing.md),
            Wrap(
              spacing: AppSpacing.sm,
              children: [
                for (final doc in latest.documents)
                  OutlinedButton.icon(
                    icon: const Icon(Icons.picture_as_pdf_outlined),
                    label: Text(
                      '${l10n.quoteDocumentDownloadCta} · ${doc.locale.toUpperCase()}',
                    ),
                    onPressed: () => context.push(
                      '/quotes/${quote.id}/versions/${latest.versionId}/document?locale=${doc.locale}',
                    ),
                  ),
              ],
            ),
          ],
          const SizedBox(height: AppSpacing.md),
          QuoteActionsToolbar(
            actions: quote.actions,
            busyAction: state.busyAction,
            onAction: (kind) => _onAction(context, kind, quote),
          ),
        ],
      ),
    );
  }

  Future<void> _onAction(
    BuildContext context,
    QuoteActionKind kind,
    QuoteDetail quote,
  ) async {
    final l10n = AppLocalizations.of(context);
    final bloc = context.read<QuoteDetailBloc>();
    // Template save needs a name; everything else takes an optional note.
    if (kind == QuoteActionKind.saveAsTemplate) {
      final name = await _promptText(
        context,
        title: l10n.quoteActionSaveAsTemplate,
        label: l10n.quoteTemplateNameLabel,
        initial: quote.quoteNumber,
      );
      if (name == null || name.trim().isEmpty) return;
      bloc.add(
          QuoteDetailActionRequested(kind: kind, templateName: name.trim()));
      return;
    }
    final isRejectOrRevision = kind == QuoteActionKind.rejectAcceptance ||
        kind == QuoteActionKind.requestRevision;
    final note = await _promptText(
      context,
      title: _labelFor(l10n, kind),
      label: isRejectOrRevision
          ? l10n.quoteActionNoteRequired
          : l10n.quoteActionNoteLabel,
      required: isRejectOrRevision,
    );
    if (isRejectOrRevision && (note == null || note.trim().isEmpty)) return;
    bloc.add(QuoteDetailActionRequested(kind: kind, note: note));
  }

  String _labelFor(AppLocalizations l10n, QuoteActionKind kind) {
    return switch (kind) {
      QuoteActionKind.submitAcceptance => l10n.quoteActionSubmitAcceptance,
      QuoteActionKind.finalizeAcceptance => l10n.quoteActionFinalizeAcceptance,
      QuoteActionKind.rejectAcceptance => l10n.quoteActionRejectAcceptance,
      QuoteActionKind.requestRevision => l10n.quoteActionRequestRevision,
      QuoteActionKind.withdraw => l10n.quoteActionWithdraw,
      QuoteActionKind.saveAsTemplate => l10n.quoteActionSaveAsTemplate,
    };
  }

  Future<String?> _promptText(
    BuildContext context, {
    required String title,
    required String label,
    String? initial,
    bool required = false,
  }) async {
    final l10n = AppLocalizations.of(context);
    final controller = TextEditingController(text: initial ?? '');
    return showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(title),
        content: TextField(
          controller: controller,
          decoration: InputDecoration(labelText: label),
          maxLines: 3,
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(null),
            child: Text(l10n.quoteActionCancel),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(controller.text),
            child: Text(l10n.quoteActionConfirm),
          ),
        ],
      ),
    );
  }
}

class _LatestVersionCard extends StatelessWidget {
  const _LatestVersionCard({required this.version, required this.dateFmt});
  final QuoteVersion version;
  final DateFormat dateFmt;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.quoteDetailPublishedAt(
                dateFmt.format(version.publishedAt.toLocal()),
              ),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            if (version.validUntil != null) ...[
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.quoteDetailValidUntil(
                  dateFmt.format(version.validUntil!.toLocal()),
                ),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
            const Divider(height: AppSpacing.lg),
            for (final line in version.lines)
              Padding(
                padding: const EdgeInsets.only(bottom: AppSpacing.xs),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        line.name,
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                    ),
                    Text(
                      l10n.quoteDetailLineRow(
                        line.qty,
                        '${line.unitPrice} ${version.totals.currency}',
                      ),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(width: AppSpacing.sm),
                    Text(
                      '${line.lineTotal} ${version.totals.currency}',
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                            fontWeight: FontWeight.w600,
                          ),
                    ),
                  ],
                ),
              ),
            const Divider(height: AppSpacing.lg),
            _totalRow(context, l10n.quoteDetailSubtotal,
                version.totals.subtotal, version.totals.currency),
            _totalRow(context, l10n.quoteDetailDiscount,
                version.totals.discount, version.totals.currency),
            _totalRow(context, l10n.quoteDetailTax, version.totals.tax,
                version.totals.currency),
            _totalRow(
              context,
              l10n.quoteDetailGrandTotal,
              version.totals.grandTotal,
              version.totals.currency,
              bold: true,
            ),
            if (version.terms.isNotEmpty) ...[
              const Divider(height: AppSpacing.lg),
              Text(
                l10n.quoteDetailTermsLabel,
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                version.terms,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _totalRow(
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
