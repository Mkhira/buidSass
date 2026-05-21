import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/invoices/bloc/invoice_preview_bloc.dart';
import 'package:customer_flutter/features/invoices/data/invoices_gateway.dart';
import 'package:customer_flutter/features/invoices/data/models/invoice_models.dart';
import 'package:flutter_test/flutter_test.dart';

class _Gateway implements InvoicesGateway {
  _Gateway(this._preview);
  final InvoicePreview _preview;

  @override
  Future<InvoicePreview> getPreview(String orderId) async => _preview;
  @override
  Future<Uint8List> getPdf(String orderId) => throw UnimplementedError();
}

class _NotFoundGateway implements InvoicesGateway {
  @override
  Future<InvoicePreview> getPreview(String orderId) async =>
      throw const NotFoundFailure(
          code: 'http.404', message: 'not found', correlationId: 'cid-1');
  @override
  Future<Uint8List> getPdf(String orderId) => throw UnimplementedError();
}

class _ErrGateway implements InvoicesGateway {
  @override
  Future<InvoicePreview> getPreview(String orderId) async =>
      throw Exception('boom');
  @override
  Future<Uint8List> getPdf(String orderId) => throw UnimplementedError();
}

InvoicePreview _preview() => InvoicePreview(
      invoiceNumber: 'INV-1',
      issuedAt: DateTime.utc(2026, 5, 1),
      currency: 'SAR',
      billing: const InvoiceBilling(name: 'Test', address: '1 St'),
      lines: const [
        InvoiceLine(
          name: 'A',
          qty: 1,
          unitPrice: '100',
          taxRate: '0.15',
          lineTotal: '115',
        ),
      ],
      totals: const InvoiceTotals(
          subtotal: '100', taxTotal: '15', grandTotal: '115'),
    );

void main() {
  blocTest<InvoicePreviewBloc, InvoicePreviewState>(
    'loads preview on Started',
    build: () =>
        InvoicePreviewBloc(gateway: _Gateway(_preview()), orderId: 'o-1'),
    act: (bloc) => bloc.add(const InvoicePreviewStarted()),
    expect: () => [
      isA<InvoicePreviewLoading>(),
      isA<InvoicePreviewLoaded>()
          .having((s) => s.preview.invoiceNumber, 'invoiceNumber', 'INV-1'),
    ],
  );

  blocTest<InvoicePreviewBloc, InvoicePreviewState>(
    '404 from getPreview emits Unavailable (BR-6)',
    build: () =>
        InvoicePreviewBloc(gateway: _NotFoundGateway(), orderId: 'o-1'),
    act: (bloc) => bloc.add(const InvoicePreviewStarted()),
    expect: () => [
      isA<InvoicePreviewLoading>(),
      isA<InvoicePreviewUnavailable>(),
    ],
  );

  blocTest<InvoicePreviewBloc, InvoicePreviewState>(
    'generic error emits Failure',
    build: () => InvoicePreviewBloc(gateway: _ErrGateway(), orderId: 'o-1'),
    act: (bloc) => bloc.add(const InvoicePreviewStarted()),
    expect: () => [
      isA<InvoicePreviewLoading>(),
      isA<InvoicePreviewFailure>(),
    ],
  );
}
