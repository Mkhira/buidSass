import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';

/// Local cache for downloaded invoice PDFs.
///
/// Logical key per plan.md: `(orderId, invoiceNumber, issuedAt)`. The
/// on-disk filename encodes only `orderId` + `invoiceNumber`; `issuedAt`
/// is the eviction trigger — when the preview's `issuedAt` changes the
/// cached file is overwritten on next download. The sweeper drops files
/// older than 30 days on app start.
class InvoicePdfCache {
  InvoicePdfCache({Future<Directory> Function()? tempDirProvider})
      : _tempDir = tempDirProvider ?? getTemporaryDirectory;

  final Future<Directory> Function() _tempDir;

  Future<Directory> _dir() async {
    final base = await _tempDir();
    final dir = Directory('${base.path}/invoices');
    if (!dir.existsSync()) {
      await dir.create(recursive: true);
    }
    return dir;
  }

  Future<File> fileFor({
    required String orderId,
    required String invoiceNumber,
  }) async {
    final dir = await _dir();
    // Sanitize — order and invoice numbers may contain characters that
    // platform file systems reject. We replace anything outside
    // [A-Za-z0-9_-] with `_` so the filename stays deterministic
    // without escaping.
    final safe =
        '${_sanitize(orderId)}-${_sanitize(invoiceNumber)}.pdf';
    return File('${dir.path}/$safe');
  }

  /// Write [bytes] for the given key. Overwrites if the file already
  /// exists (eviction-by-rewrite when `issuedAt` changes).
  Future<File> store({
    required String orderId,
    required String invoiceNumber,
    required Uint8List bytes,
  }) async {
    final f = await fileFor(orderId: orderId, invoiceNumber: invoiceNumber);
    await f.writeAsBytes(bytes, flush: true);
    return f;
  }

  /// Sweep the cache directory, removing files older than [maxAge].
  /// Called from `main()` on app start (plan §Risk 3).
  Future<int> sweep({Duration maxAge = const Duration(days: 30)}) async {
    final dir = await _dir();
    final cutoff = DateTime.now().subtract(maxAge);
    var removed = 0;
    try {
      await for (final entry in dir.list()) {
        if (entry is! File) continue;
        FileStat stat;
        try {
          stat = await entry.stat();
        } on Object {
          // File vanished between list and stat — skip.
          continue;
        }
        if (stat.modified.isBefore(cutoff)) {
          try {
            await entry.delete();
            removed += 1;
          } on Object catch (e) {
            debugPrint('InvoicePdfCache.sweep: failed to delete '
                '${entry.path}: $e');
          }
        }
      }
    } on Object catch (e) {
      // Sweeper failures must never crash app boot.
      debugPrint('InvoicePdfCache.sweep: $e');
    }
    return removed;
  }

  String _sanitize(String raw) =>
      raw.replaceAll(RegExp(r'[^A-Za-z0-9_\-]'), '_');
}
