import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';

/// Local cache for downloaded quote-version PDFs.
///
/// Logical key per data-model.md: `(quoteId, versionId, locale)`. The
/// on-disk filename encodes all three so a different locale or a new
/// version round-trips to a separate file. The sweeper drops files
/// older than 30 days on app start (shared sweep cadence with the
/// invoice cache).
class QuoteDocumentCache {
  QuoteDocumentCache({Future<Directory> Function()? tempDirProvider})
      : _tempDir = tempDirProvider ?? getTemporaryDirectory;

  final Future<Directory> Function() _tempDir;

  Future<Directory> _dir() async {
    final base = await _tempDir();
    final dir = Directory('${base.path}/quote-docs');
    if (!dir.existsSync()) {
      await dir.create(recursive: true);
    }
    return dir;
  }

  Future<File> fileFor({
    required String quoteId,
    required String versionId,
    required String locale,
  }) async {
    final dir = await _dir();
    final safe =
        '${_sanitize(quoteId)}-${_sanitize(versionId)}-${_sanitize(locale)}.pdf';
    return File('${dir.path}/$safe');
  }

  Future<File> store({
    required String quoteId,
    required String versionId,
    required String locale,
    required Uint8List bytes,
  }) async {
    final f = await fileFor(
      quoteId: quoteId,
      versionId: versionId,
      locale: locale,
    );
    await f.writeAsBytes(bytes, flush: true);
    return f;
  }

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
          continue;
        }
        if (stat.modified.isBefore(cutoff)) {
          try {
            await entry.delete();
            removed += 1;
          } on Object catch (e) {
            debugPrint('QuoteDocumentCache.sweep: failed to delete '
                '${entry.path}: $e');
          }
        }
      }
    } on Object catch (e) {
      debugPrint('QuoteDocumentCache.sweep: $e');
    }
    return removed;
  }

  String _sanitize(String raw) =>
      raw.replaceAll(RegExp(r'[^A-Za-z0-9_\-]'), '_');
}
