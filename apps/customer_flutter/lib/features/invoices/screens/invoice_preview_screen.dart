import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/invoice_preview_bloc.dart';
import '../data/models/invoice_models.dart';

/// S-6.4 — invoice preview. JSON-driven render of the tax invoice
/// header, billing block, line items (with VAT rate explicit per
/// Principle 18 + BR-7), totals, and a Download PDF CTA that routes to
/// S-6.5.
///
/// 404 from the preview endpoint renders the dedicated
/// "available after payment captures" empty state (BR-6).
class InvoicePreviewScreen extends StatelessWidget {
  const InvoicePreviewScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.invoicePreviewTitle)),
      body: BlocBuilder<InvoicePreviewBloc, InvoicePreviewState>(
        builder: (context, state) {
          return switch (state) {
            InvoicePreviewLoading() =>
              LoadingState(semanticsLabel: l10n.commonLoading),
            InvoicePreviewUnavailable() => EmptyState(
                title: l10n.invoiceUnavailableTitle,
                body: l10n.invoiceUnavailableBody,
                icon: Icons.receipt_long_outlined,
              ),
            InvoicePreviewFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<InvoicePreviewBloc>()
                    .add(const InvoicePreviewRefreshed()),
                retryLabel: l10n.commonRetry,
              ),
            InvoicePreviewLoaded(:final preview) => RefreshIndicator(
                onRefresh: () async {
                  final bloc = context.read<InvoicePreviewBloc>();
                  bloc.add(const InvoicePreviewRefreshed());
                  await bloc.stream
                      .firstWhere((s) => s is! InvoicePreviewLoading);
                },
                child: _LoadedBody(preview: preview, orderId: orderId),
              ),
          };
        },
      ),
    );
  }
}

class _LoadedBody extends StatelessWidget {
  const _LoadedBody({required this.preview, required this.orderId});
  final InvoicePreview preview;
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final locale = Localizations.localeOf(context).toString();
    final fmt = NumberFormat.currency(
      locale: locale,
      symbol: preview.currency,
      decimalDigits: 2,
    );
    final dateFmt = DateFormat.yMMMd(locale);
    String money(String raw) => fmt.format(double.tryParse(raw) ?? 0);
    return ListView(
      padding: const EdgeInsets.all(AppSpacing.md),
      children: [
        _HeaderRow(
          label: l10n.invoicePreviewInvoiceNumberLabel,
          value: preview.invoiceNumber,
        ),
        _HeaderRow(
          label: l10n.invoicePreviewIssuedAtLabel,
          value: dateFmt.format(preview.issuedAt),
        ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.invoicePreviewBillingTitle),
        ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(preview.billing.name),
          subtitle: Text(preview.billing.address),
        ),
        if (preview.billing.vatNumber != null &&
            preview.billing.vatNumber!.isNotEmpty)
          _HeaderRow(
            label: l10n.invoicePreviewVatNumberLabel,
            value: preview.billing.vatNumber!,
          ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.invoicePreviewLinesTitle),
        for (final line in preview.lines)
          ListTile(
            contentPadding: EdgeInsets.zero,
            title: Text(line.name),
            subtitle: Text([
              '${line.qty} × ${money(line.unitPrice)}',
              // Per Principle 18 + S-6.4 AC: VAT rate explicit on
              // every line. We never recompute the line total — the
              // server's value is the source of truth (BR-7).
              l10n.invoicePreviewLineVatLabel(_pct(line.taxRate)),
            ].join(' · ')),
            trailing: Text(money(line.lineTotal)),
          ),
        const Divider(height: AppSpacing.lg),
        _SectionTitle(text: l10n.invoicePreviewTotalsTitle),
        _TotalsRow(
          label: l10n.invoicePreviewSubtotalLabel,
          value: money(preview.totals.subtotal),
        ),
        _TotalsRow(
          label: l10n.invoicePreviewTaxLabel(_invoiceVatRate(preview)),
          value: money(preview.totals.taxTotal),
        ),
        _TotalsRow(
          label: l10n.invoicePreviewGrandTotalLabel,
          value: money(preview.totals.grandTotal),
          emphasized: true,
        ),
        const SizedBox(height: AppSpacing.lg),
        FilledButton.icon(
          icon: const Icon(Icons.download_outlined),
          label: Text(l10n.invoicePreviewDownloadCta),
          onPressed: () => context.push('/o/$orderId/invoice/pdf'),
        ),
      ],
    );
  }

  /// Convert a server decimal-string rate ("0.15", "0.14") into a
  /// localized percentage string. Server is the source of truth — we
  /// never multiply by quantity.
  String _pct(String rate) {
    final v = double.tryParse(rate) ?? 0;
    return '${(v * 100).toStringAsFixed(0)}%';
  }

  /// Header VAT rate — use the first line's rate when all lines share
  /// it (the common case in V1). If lines disagree (mixed-rate cart),
  /// fall back to an empty header but each line still carries its own
  /// rate in the breakdown.
  String _invoiceVatRate(InvoicePreview p) {
    if (p.lines.isEmpty) return '';
    final first = p.lines.first.taxRate;
    final allSame = p.lines.every((l) => l.taxRate == first);
    if (!allSame) return '';
    return _pct(first);
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Text(text, style: Theme.of(context).textTheme.titleSmall),
    );
  }
}

class _HeaderRow extends StatelessWidget {
  const _HeaderRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(
              child: Text(label,
                  style: Theme.of(context)
                      .textTheme
                      .bodySmall
                      ?.copyWith(color: AppColors.textSecondary))),
          Text(value, style: Theme.of(context).textTheme.bodyMedium),
        ],
      ),
    );
  }
}

class _TotalsRow extends StatelessWidget {
  const _TotalsRow({
    required this.label,
    required this.value,
    this.emphasized = false,
  });
  final String label;
  final String value;
  final bool emphasized;

  @override
  Widget build(BuildContext context) {
    final style = emphasized
        ? Theme.of(context).textTheme.titleMedium
        : Theme.of(context).textTheme.bodyMedium;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(child: Text(label, style: style)),
          Text(value, style: style),
        ],
      ),
    );
  }
}
