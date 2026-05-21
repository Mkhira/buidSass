import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/invoice_pdf_bloc.dart';

/// S-6.5 — invoice PDF download + open + share. The screen renders the
/// current bloc state. Open + Share are routed through the bloc so the
/// platform launchers (open_filex / share_plus) stay testable.
class InvoicePdfScreen extends StatelessWidget {
  const InvoicePdfScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.invoicePdfTitle)),
      body: BlocBuilder<InvoicePdfBloc, InvoicePdfState>(
        builder: (context, state) {
          return switch (state) {
            InvoicePdfIdle() ||
            InvoicePdfDownloading() =>
              LoadingState(semanticsLabel: l10n.invoicePdfDownloading),
            InvoicePdfUnavailable() => EmptyState(
                title: l10n.invoiceUnavailableTitle,
                body: l10n.invoiceUnavailableBody,
                icon: Icons.receipt_long_outlined,
              ),
            InvoicePdfDiskFull() => EmptyState(
                title: l10n.invoicePdfDiskFullTitle,
                body: l10n.invoicePdfDiskFullBody,
                icon: Icons.sd_storage_outlined,
                action: FilledButton(
                  onPressed: () => context
                      .read<InvoicePdfBloc>()
                      .add(const InvoicePdfDownloadRequested()),
                  child: Text(l10n.commonRetry),
                ),
              ),
            InvoicePdfFailure(:final reason) => ErrorState(
                title: l10n.commonErrorTitle,
                body: reason,
                onRetry: () => context
                    .read<InvoicePdfBloc>()
                    .add(const InvoicePdfDownloadRequested()),
                retryLabel: l10n.commonRetry,
              ),
            InvoicePdfReady(:final invoiceNumber) =>
              _ReadyBody(orderId: orderId, invoiceNumber: invoiceNumber),
          };
        },
      ),
    );
  }
}

class _ReadyBody extends StatelessWidget {
  const _ReadyBody({required this.orderId, required this.invoiceNumber});
  final String orderId;
  final String invoiceNumber;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final localeCode = Localizations.localeOf(context).languageCode;
    // Filename — localized per S-6.5 AC. Uses the orderId as the
    // suffix (orderNumber would require an extra fetch).
    final filename = localeCode == 'ar'
        ? l10n.invoicePdfFilenameAr(orderId)
        : l10n.invoicePdfFilenameEn(orderId);
    return Padding(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(Icons.picture_as_pdf_outlined, size: 72),
          const SizedBox(height: AppSpacing.md),
          Text(l10n.invoicePdfReadyTitle,
              style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: AppSpacing.xs),
          Text(l10n.invoicePdfReadyBody,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: AppSpacing.xs),
          Text(invoiceNumber,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: AppColors.textSecondary,
                  )),
          const SizedBox(height: AppSpacing.lg),
          Wrap(
            spacing: AppSpacing.sm,
            runSpacing: AppSpacing.sm,
            alignment: WrapAlignment.center,
            children: [
              FilledButton.icon(
                icon: const Icon(Icons.open_in_new),
                label: Text(l10n.invoicePdfOpenCta),
                onPressed: () => context
                    .read<InvoicePdfBloc>()
                    .add(const InvoicePdfOpenRequested()),
              ),
              OutlinedButton.icon(
                icon: const Icon(Icons.share_outlined),
                label: Text(l10n.invoicePdfShareCta),
                onPressed: () => context
                    .read<InvoicePdfBloc>()
                    .add(InvoicePdfShareRequested(filename)),
              ),
              TextButton.icon(
                icon: const Icon(Icons.refresh),
                label: Text(l10n.invoicePdfRedownloadCta),
                onPressed: () => context
                    .read<InvoicePdfBloc>()
                    .add(const InvoicePdfDownloadRequested()),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
