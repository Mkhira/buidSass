import 'dart:typed_data';

import 'models/invoice_models.dart';

/// InvoicesGateway — the 2 customer-tagged ops in
/// `openapi.invoices.json`:
///
/// * `GET /v1/customer/orders/{orderId}/invoice` — JSON preview.
/// * `GET /v1/customer/orders/{orderId}/invoice.pdf` — binary PDF body.
///
/// PDF caching + share-sheet behavior lives in the bloc layer (see
/// `InvoicePdfBloc`). The gateway hands back raw bytes only.
abstract class InvoicesGateway {
  /// Fetch the structured invoice preview. Throws on 404 (invoice not
  /// yet available — e.g. bank transfer pending). The bloc maps 404 to
  /// the "available after payment captures" empty state (BR-6).
  Future<InvoicePreview> getPreview(String orderId);

  /// Fetch the binary PDF for the order's invoice. Returns the full
  /// body as a `Uint8List`. Throws on 404 + other HTTP failures so the
  /// bloc can surface them as `pdf-failed`.
  Future<Uint8List> getPdf(String orderId);
}
