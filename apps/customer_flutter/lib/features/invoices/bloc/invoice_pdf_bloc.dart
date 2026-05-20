import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:open_filex/open_filex.dart';
import 'package:share_plus/share_plus.dart';

import '../../../core/error/failure.dart';
import '../data/invoice_pdf_cache.dart';
import '../data/invoices_gateway.dart';

// ============================================================
// Platform adapters (overridable for tests)
// ============================================================

typedef InvoicePdfOpener = Future<void> Function(String path);
typedef InvoicePdfSharer = Future<void> Function({
  required String path,
  required String filename,
});

Future<void> _defaultOpen(String path) async {
  final res = await OpenFilex.open(path);
  if (res.type != ResultType.done) {
    throw Exception('open_filex failed: ${res.message}');
  }
}

Future<void> _defaultShare({
  required String path,
  required String filename,
}) async {
  await Share.shareXFiles(
    [XFile(path, name: filename)],
    subject: filename,
  );
}

// ============================================================
// Events
// ============================================================

@immutable
sealed class InvoicePdfEvent {
  const InvoicePdfEvent();
}

class InvoicePdfDownloadRequested extends InvoicePdfEvent {
  const InvoicePdfDownloadRequested({this.invoiceNumber});

  /// Optional override — when the screen comes from the preview it
  /// already knows the invoiceNumber + issuedAt; otherwise the bloc
  /// uses the gateway's preview as a discovery call.
  final String? invoiceNumber;
}

class InvoicePdfOpenRequested extends InvoicePdfEvent {
  const InvoicePdfOpenRequested();
}

class InvoicePdfShareRequested extends InvoicePdfEvent {
  const InvoicePdfShareRequested(this.localizedFilename);

  /// Locale-aware filename per S-6.5 AC. Resolved by the screen via
  /// `l10n.invoicePdfFilenameAr / invoicePdfFilenameEn`.
  final String localizedFilename;
}

// ============================================================
// States
// ============================================================

@immutable
sealed class InvoicePdfState {
  const InvoicePdfState();
}

class InvoicePdfIdle extends InvoicePdfState {
  const InvoicePdfIdle();
}

class InvoicePdfDownloading extends InvoicePdfState {
  const InvoicePdfDownloading();
}

class InvoicePdfReady extends InvoicePdfState {
  const InvoicePdfReady({
    required this.path,
    required this.invoiceNumber,
    this.orderNumberForFilename,
  });

  final String path;
  final String invoiceNumber;
  final String? orderNumberForFilename;
}

class InvoicePdfUnavailable extends InvoicePdfState {
  const InvoicePdfUnavailable();
}

class InvoicePdfDiskFull extends InvoicePdfState {
  const InvoicePdfDiskFull();
}

class InvoicePdfFailure extends InvoicePdfState {
  const InvoicePdfFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

// ============================================================
// Bloc
// ============================================================

/// Bloc for S-6.5 invoice PDF download + open + share. Owns the local
/// cache lookup and the platform launch. Cache key per plan.md is
/// `(orderId, invoiceNumber, issuedAt)`; eviction is overwrite-on-next-
/// download when `issuedAt` changes.
class InvoicePdfBloc extends Bloc<InvoicePdfEvent, InvoicePdfState> {
  InvoicePdfBloc({
    required InvoicesGateway gateway,
    required this.orderId,
    InvoicePdfCache? cache,
    InvoicePdfOpener? opener,
    InvoicePdfSharer? sharer,
  })  : _gateway = gateway,
        _cache = cache ?? InvoicePdfCache(),
        _open = opener ?? _defaultOpen,
        _share = sharer ?? _defaultShare,
        super(const InvoicePdfIdle()) {
    on<InvoicePdfDownloadRequested>(_onDownload);
    on<InvoicePdfOpenRequested>(_onOpen);
    on<InvoicePdfShareRequested>(_onShare);
  }

  final InvoicesGateway _gateway;
  final String orderId;
  final InvoicePdfCache _cache;
  final InvoicePdfOpener _open;
  final InvoicePdfSharer _share;

  Future<void> _onDownload(
    InvoicePdfDownloadRequested event,
    Emitter<InvoicePdfState> emit,
  ) async {
    emit(const InvoicePdfDownloading());
    try {
      // Resolve invoiceNumber via the preview — preview is cheap and
      // also surfaces the 404 (not-yet-available) branch consistently.
      // If the screen handed an override we still consult the preview
      // to verify it still exists.
      final preview = await _gateway.getPreview(orderId);
      final bytes = await _gateway.getPdf(orderId);
      final file = await _cache.store(
        orderId: orderId,
        invoiceNumber: preview.invoiceNumber,
        bytes: bytes,
      );
      emit(InvoicePdfReady(
        path: file.path,
        invoiceNumber: preview.invoiceNumber,
      ));
    } on NotFoundFailure {
      emit(const InvoicePdfUnavailable());
    } on FileSystemException catch (e) {
      // Disk full / quota / permission — the os errno surfaces here.
      // We don't try to introspect the platform errno; the screen
      // shows a generic "not enough space" message per S-6.5 edge case.
      debugPrint('InvoicePdfBloc.download FileSystem: $e');
      emit(const InvoicePdfDiskFull());
    } on Failure catch (f) {
      emit(
          InvoicePdfFailure(reason: f.message, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(InvoicePdfFailure(reason: e.toString()));
    }
  }

  Future<void> _onOpen(
    InvoicePdfOpenRequested event,
    Emitter<InvoicePdfState> emit,
  ) async {
    final s = state;
    if (s is! InvoicePdfReady) return;
    try {
      // Defensive — file may have vanished (cache sweeper between
      // download and Open press). Re-download in that case.
      final f = File(s.path);
      if (!f.existsSync()) {
        add(const InvoicePdfDownloadRequested());
        return;
      }
      await _open(s.path);
    } on Object catch (e) {
      emit(InvoicePdfFailure(reason: e.toString()));
    }
  }

  Future<void> _onShare(
    InvoicePdfShareRequested event,
    Emitter<InvoicePdfState> emit,
  ) async {
    final s = state;
    if (s is! InvoicePdfReady) return;
    try {
      final f = File(s.path);
      if (!f.existsSync()) {
        add(const InvoicePdfDownloadRequested());
        return;
      }
      await _share(path: s.path, filename: event.localizedFilename);
    } on Object catch (e) {
      emit(InvoicePdfFailure(reason: e.toString()));
    }
  }
}
