import 'dart:io';

import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../generated/l10n/app_localizations.dart';
import '../bloc/quote_document_bloc.dart';

/// S-8.6 — quote document download / open / share. Mirrors the
/// invoice PDF screen.
class QuoteDocumentScreen extends StatelessWidget {
  const QuoteDocumentScreen({
    super.key,
    required this.quoteId,
    required this.versionId,
    required this.initialLocale,
  });

  final String quoteId;
  final String versionId;
  final String initialLocale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return AppScaffold(
      appBar: AppBar(title: Text(l10n.quoteDocumentTitle)),
      body: BlocBuilder<QuoteDocumentBloc, QuoteDocumentState>(
        builder: (context, state) {
          return SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _LocaleSwitcher(
                    quoteId: quoteId,
                    versionId: versionId,
                    initialLocale: initialLocale,
                    currentLocale: state is QuoteDocumentReady
                        ? state.locale
                        : initialLocale,
                  ),
                  const SizedBox(height: AppSpacing.md),
                  Expanded(child: _Body(state: state)),
                  _BottomActions(state: state),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}

class _LocaleSwitcher extends StatelessWidget {
  const _LocaleSwitcher({
    required this.quoteId,
    required this.versionId,
    required this.initialLocale,
    required this.currentLocale,
  });
  final String quoteId;
  final String versionId;
  final String initialLocale;
  final String currentLocale;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Row(
      children: [
        Expanded(
          child: Text(
            l10n.quoteDocumentLocaleLabel,
            style: Theme.of(context).textTheme.titleSmall,
          ),
        ),
        SegmentedButton<String>(
          segments: const [
            ButtonSegment(value: 'en', label: Text('EN')),
            ButtonSegment(value: 'ar', label: Text('AR')),
          ],
          selected: {currentLocale},
          onSelectionChanged: (s) {
            context.read<QuoteDocumentBloc>().add(
                  QuoteDocumentDownloadRequested(
                    quoteId: quoteId,
                    versionId: versionId,
                    locale: s.first,
                  ),
                );
          },
        ),
      ],
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.state});
  final QuoteDocumentState state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return switch (state) {
      QuoteDocumentIdle() => EmptyState(
          title: l10n.quoteDocumentTitle,
          body: l10n.quoteDocumentReadyBody,
          icon: Icons.picture_as_pdf_outlined,
        ),
      QuoteDocumentDownloading() => LoadingState(
          semanticsLabel: l10n.quoteDocumentDownloading,
        ),
      QuoteDocumentReady() => EmptyState(
          title: l10n.quoteDocumentReadyTitle,
          body: l10n.quoteDocumentReadyBody,
          icon: Icons.check_circle_outline,
        ),
      QuoteDocumentUnavailable() => EmptyState(
          title: l10n.quoteDocumentUnavailableTitle,
          body: l10n.quoteDocumentUnavailableBody,
          icon: Icons.hourglass_empty_outlined,
        ),
      QuoteDocumentFailure() => ErrorState(
          title: l10n.commonErrorTitle,
          body: l10n.commonErrorBody,
        ),
    };
  }
}

class _BottomActions extends StatelessWidget {
  const _BottomActions({required this.state});
  final QuoteDocumentState state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final ready = state is QuoteDocumentReady;
    final file = ready ? (state as QuoteDocumentReady).file : null;
    return Row(
      children: [
        Expanded(
          child: OutlinedButton.icon(
            icon: const Icon(Icons.open_in_new),
            label: Text(l10n.quoteDocumentOpenCta),
            onPressed: !ready
                ? null
                : () => context
                    .read<QuoteDocumentBloc>()
                    .add(const QuoteDocumentOpenRequested()),
          ),
        ),
        const SizedBox(width: AppSpacing.sm),
        Expanded(
          child: OutlinedButton.icon(
            icon: const Icon(Icons.share),
            label: Text(l10n.quoteDocumentShareCta),
            onPressed: !ready
                ? null
                : () => context
                    .read<QuoteDocumentBloc>()
                    .add(const QuoteDocumentShareRequested()),
          ),
        ),
        if (file is File) const SizedBox(),
      ],
    );
  }
}
