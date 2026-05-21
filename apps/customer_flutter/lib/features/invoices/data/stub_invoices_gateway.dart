import 'dart:typed_data';

import 'invoices_gateway.dart';
import 'models/invoice_models.dart';

/// Deterministic in-memory [InvoicesGateway] for offline dev.
///
/// Behavior:
/// * `getPreview` always returns a fully-populated KSA preview (15% VAT)
///   so the UI can be exercised without a live backend.
/// * `getPdf` returns a tiny synthetic PDF byte sequence (the `%PDF-1.4`
///   header + EOF marker). It's not a real PDF — the dev/preview
///   `Open` path still launches the system viewer, which displays
///   whatever the bytes contain. Good enough for smoke; never shipped
///   to production builds.
class StubInvoicesGateway implements InvoicesGateway {
  StubInvoicesGateway({DateTime? now}) : _now = now ?? _seedNow;

  static final DateTime _seedNow = DateTime.utc(2026, 5, 20);

  final DateTime _now;

  @override
  Future<InvoicePreview> getPreview(String orderId) async {
    return InvoicePreview(
      invoiceNumber: 'INV-2026-05-${orderId.hashCode.abs() % 1000000}',
      issuedAt: _now,
      currency: 'SAR',
      billing: const InvoiceBilling(
        name: 'Test Customer',
        address: '123 Olaya St, Riyadh',
        vatNumber: '300000000000003',
      ),
      lines: const [
        InvoiceLine(
          name: 'Dental gel',
          qty: 2,
          unitPrice: '60.00',
          taxRate: '0.15',
          lineTotal: '138.00',
        ),
        InvoiceLine(
          name: 'Whitening kit',
          qty: 1,
          unitPrice: '120.00',
          taxRate: '0.15',
          lineTotal: '138.00',
        ),
      ],
      totals: const InvoiceTotals(
        subtotal: '240.00',
        taxTotal: '36.00',
        grandTotal: '276.00',
      ),
      downloadUrl: '/v1/customer/orders/$orderId/invoice.pdf',
    );
  }

  @override
  Future<Uint8List> getPdf(String orderId) async {
    // Minimal valid-ish PDF stub: header + a comment line + EOF.
    // Real viewers will render "blank document" — enough to verify the
    // Open/Share branches without a backend.
    const header = '%PDF-1.4\n% stub invoice — orderId stub\n%%EOF\n';
    return Uint8List.fromList(header.codeUnits);
  }
}
