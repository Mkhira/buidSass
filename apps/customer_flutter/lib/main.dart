import 'dart:async';

import 'package:flutter/material.dart';

import 'app/app.dart';
import 'app/di.dart';
import 'features/invoices/data/invoice_pdf_cache.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await bootstrap();
  // Phase 6 — sweep stale invoice PDFs from the temp cache. Async,
  // not awaited: cache cleanup must NEVER block app boot. The sweeper
  // itself swallows IO errors (see [InvoicePdfCache.sweep]).
  unawaited(InvoicePdfCache().sweep().catchError((Object e) {
    // Cache cleanup failures must never break boot; swallow + log.
    // ignore: avoid_print
    print('Invoice PDF cache sweep failed: $e');
    return 0;
  }));
  runApp(const CustomerApp());
}
