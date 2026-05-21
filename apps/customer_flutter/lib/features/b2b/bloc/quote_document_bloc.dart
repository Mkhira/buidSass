import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/quote_document_cache.dart';
import '../data/quotes_gateway.dart';

@immutable
sealed class QuoteDocumentEvent {
  const QuoteDocumentEvent();
}

class QuoteDocumentDownloadRequested extends QuoteDocumentEvent {
  const QuoteDocumentDownloadRequested({
    required this.quoteId,
    required this.versionId,
    required this.locale,
  });
  final String quoteId;
  final String versionId;
  final String locale;
}

class QuoteDocumentOpenRequested extends QuoteDocumentEvent {
  const QuoteDocumentOpenRequested();
}

class QuoteDocumentShareRequested extends QuoteDocumentEvent {
  const QuoteDocumentShareRequested();
}

@immutable
sealed class QuoteDocumentState {
  const QuoteDocumentState();
}

class QuoteDocumentIdle extends QuoteDocumentState {
  const QuoteDocumentIdle();
}

class QuoteDocumentDownloading extends QuoteDocumentState {
  const QuoteDocumentDownloading();
}

class QuoteDocumentReady extends QuoteDocumentState {
  const QuoteDocumentReady({required this.file, required this.locale});
  final File file;
  final String locale;
}

class QuoteDocumentUnavailable extends QuoteDocumentState {
  const QuoteDocumentUnavailable();
}

class QuoteDocumentFailure extends QuoteDocumentState {
  const QuoteDocumentFailure({required this.reason});
  final String reason;
}

/// Bloc for S-8.6 — quote document download. Mirrors InvoicePdfBloc:
/// download → temp-cache → open/share. Cache key is `(quoteId,
/// versionId, locale)`; a different locale triggers a fresh download.
class QuoteDocumentBloc extends Bloc<QuoteDocumentEvent, QuoteDocumentState> {
  QuoteDocumentBloc({
    required QuotesGateway gateway,
    QuoteDocumentCache? cache,
    Future<void> Function(File file)? opener,
    Future<void> Function(File file)? sharer,
  })  : _gateway = gateway,
        _cache = cache ?? QuoteDocumentCache(),
        _opener = opener,
        _sharer = sharer,
        super(const QuoteDocumentIdle()) {
    on<QuoteDocumentDownloadRequested>(_onDownload);
    on<QuoteDocumentOpenRequested>(_onOpen);
    on<QuoteDocumentShareRequested>(_onShare);
  }

  final QuotesGateway _gateway;
  final QuoteDocumentCache _cache;
  final Future<void> Function(File file)? _opener;
  final Future<void> Function(File file)? _sharer;

  Future<void> _onDownload(
    QuoteDocumentDownloadRequested e,
    Emitter<QuoteDocumentState> emit,
  ) async {
    emit(const QuoteDocumentDownloading());
    try {
      final bytes = await _gateway.downloadDocument(
        quoteId: e.quoteId,
        versionId: e.versionId,
        locale: e.locale,
      );
      final file = await _cache.store(
        quoteId: e.quoteId,
        versionId: e.versionId,
        locale: e.locale,
        bytes: bytes,
      );
      emit(QuoteDocumentReady(file: file, locale: e.locale));
    } on Object catch (err) {
      final msg = err.toString();
      if (msg.contains('404') || msg.contains('not_found')) {
        emit(const QuoteDocumentUnavailable());
        return;
      }
      emit(const QuoteDocumentFailure(reason: 'quote.document_failed'));
    }
  }

  Future<void> _onOpen(
    QuoteDocumentOpenRequested e,
    Emitter<QuoteDocumentState> emit,
  ) async {
    final s = state;
    if (s is! QuoteDocumentReady || _opener == null) return;
    try {
      await _opener(s.file);
    } on Object catch (_) {
      emit(const QuoteDocumentFailure(reason: 'quote.document_open_failed'));
    }
  }

  Future<void> _onShare(
    QuoteDocumentShareRequested e,
    Emitter<QuoteDocumentState> emit,
  ) async {
    final s = state;
    if (s is! QuoteDocumentReady || _sharer == null) return;
    try {
      await _sharer(s.file);
    } on Object catch (_) {
      emit(const QuoteDocumentFailure(reason: 'quote.document_share_failed'));
    }
  }
}
