import 'dart:io';
import 'dart:typed_data';

import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/invoices/bloc/invoice_pdf_bloc.dart';
import 'package:customer_flutter/features/invoices/data/invoice_pdf_cache.dart';
import 'package:customer_flutter/features/invoices/data/invoices_gateway.dart';
import 'package:customer_flutter/features/invoices/data/models/invoice_models.dart';
import 'package:flutter_test/flutter_test.dart';

class _OkGateway implements InvoicesGateway {
  _OkGateway();

  int previewCalls = 0;
  int pdfCalls = 0;

  @override
  Future<InvoicePreview> getPreview(String orderId) async {
    previewCalls += 1;
    return InvoicePreview(
      invoiceNumber: 'INV-1',
      issuedAt: DateTime.utc(2026, 5, 1),
      currency: 'SAR',
      billing: const InvoiceBilling(name: 'Test', address: '1 St'),
      lines: const [],
      totals:
          const InvoiceTotals(subtotal: '0', taxTotal: '0', grandTotal: '0'),
    );
  }

  @override
  Future<Uint8List> getPdf(String orderId) async {
    pdfCalls += 1;
    return Uint8List.fromList(const [0x25, 0x50, 0x44, 0x46]);
  }
}

class _NotFoundGateway implements InvoicesGateway {
  @override
  Future<InvoicePreview> getPreview(String orderId) async =>
      throw const NotFoundFailure(
          code: 'http.404', message: 'not found', correlationId: 'cid');
  @override
  Future<Uint8List> getPdf(String orderId) async => Uint8List(0);
}

void main() {
  late Directory tempDir;

  setUp(() async {
    tempDir = await Directory.systemTemp.createTemp('inv-test-');
  });

  tearDown(() async {
    try {
      await tempDir.delete(recursive: true);
    } on Object {
      // ignore
    }
  });

  test('download writes bytes to cache and emits Ready', () async {
    final gw = _OkGateway();
    final cache = InvoicePdfCache(tempDirProvider: () async => tempDir);
    var openCalls = 0;
    final bloc = InvoicePdfBloc(
      gateway: gw,
      orderId: 'order-1',
      cache: cache,
      opener: (_) async {
        openCalls += 1;
      },
      sharer: ({required path, required filename}) async {},
    );
    bloc.add(const InvoicePdfDownloadRequested());
    final ready = await bloc.stream.firstWhere((s) => s is InvoicePdfReady);
    expect(ready, isA<InvoicePdfReady>());
    final readyState = ready as InvoicePdfReady;
    expect(File(readyState.path).existsSync(), isTrue);
    // Round-trip Open through the injected opener.
    bloc.add(const InvoicePdfOpenRequested());
    await Future<void>.delayed(Duration.zero);
    expect(openCalls, 1);
    await bloc.close();
  });

  test('404 from preview emits Unavailable', () async {
    final bloc = InvoicePdfBloc(
      gateway: _NotFoundGateway(),
      orderId: 'order-1',
      cache: InvoicePdfCache(tempDirProvider: () async => tempDir),
      opener: (_) async {},
      sharer: ({required path, required filename}) async {},
    );
    bloc.add(const InvoicePdfDownloadRequested());
    final s =
        await bloc.stream.firstWhere((s) => s is InvoicePdfUnavailable);
    expect(s, isA<InvoicePdfUnavailable>());
    await bloc.close();
  });

  test('share invokes the injected sharer with localized filename',
      () async {
    String? sharedPath;
    String? sharedFilename;
    final bloc = InvoicePdfBloc(
      gateway: _OkGateway(),
      orderId: 'order-1',
      cache: InvoicePdfCache(tempDirProvider: () async => tempDir),
      opener: (_) async {},
      sharer: ({required path, required filename}) async {
        sharedPath = path;
        sharedFilename = filename;
      },
    );
    bloc.add(const InvoicePdfDownloadRequested());
    await bloc.stream.firstWhere((s) => s is InvoicePdfReady);
    bloc.add(const InvoicePdfShareRequested('invoice-order-1.pdf'));
    await Future<void>.delayed(Duration.zero);
    expect(sharedFilename, 'invoice-order-1.pdf');
    expect(sharedPath, isNotNull);
    expect(sharedPath!.endsWith('.pdf'), isTrue);
    await bloc.close();
  });

  group('InvoicePdfCache', () {
    test('store writes bytes and stable filename', () async {
      final cache = InvoicePdfCache(tempDirProvider: () async => tempDir);
      final f = await cache.store(
        orderId: 'o-1',
        invoiceNumber: 'INV-1',
        bytes: Uint8List.fromList(const [1, 2, 3]),
      );
      expect(f.path.endsWith('o-1-INV-1.pdf'), isTrue);
      expect(await f.readAsBytes(), equals(const [1, 2, 3]));
    });

    test('store sanitizes unsafe characters in the filename', () async {
      final cache = InvoicePdfCache(tempDirProvider: () async => tempDir);
      final f = await cache.store(
        orderId: 'o/1',
        invoiceNumber: 'INV/1',
        bytes: Uint8List.fromList(const [1]),
      );
      // Slashes get rewritten to `_` — otherwise the path would
      // resolve into a subdirectory and writeAsBytes would fail.
      expect(f.path.endsWith('o_1-INV_1.pdf'), isTrue);
    });

    test('sweep removes files older than maxAge', () async {
      final cache = InvoicePdfCache(tempDirProvider: () async => tempDir);
      final f = await cache.store(
        orderId: 'o-1',
        invoiceNumber: 'INV-1',
        bytes: Uint8List.fromList(const [1]),
      );
      // Backdate the mtime past the 30-day cutoff.
      await f.setLastModified(DateTime.now().subtract(const Duration(days: 31)));
      final removed = await cache.sweep();
      expect(removed, 1);
      expect(f.existsSync(), isFalse);
    });
  });
}
