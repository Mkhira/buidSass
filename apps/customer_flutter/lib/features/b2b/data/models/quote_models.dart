import 'package:flutter/foundation.dart';

import '../../../checkout/data/models/checkout_models.dart' show Money;

// ============================================================
// Quotes — Phase 8 customer surface
// ============================================================
// Models parse the wire shapes spelled out in
// `specs/mobile/phase-8-b2b/{spec.md,data-model.md}`. State values are
// wire strings; UI defaults to the raw value when unknown. The 7-value
// lifecycle is the explicit state machine for quotes per Principle 24.

const Set<String> kKnownQuoteStates = {
  'draft',
  'published',
  'awaiting_acceptance',
  'awaiting_finalization',
  'accepted',
  'rejected',
  'withdrawn',
  'expired',
};

// ============================================================
// Filter + list page
// ============================================================

@immutable
class QuotesFilter {
  const QuotesFilter({this.state, this.page = 1, this.pageSize = 20});

  final String? state;
  final int page;
  final int pageSize;

  Map<String, Object?> toQuery() => {
        if (state != null) 'state': state,
        'page': page,
        'pageSize': pageSize,
      };

  QuotesFilter copyWith({
    Object? state = _sentinel,
    int? page,
    int? pageSize,
  }) {
    return QuotesFilter(
      state: identical(state, _sentinel) ? this.state : state as String?,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}

const _sentinel = Object();

@immutable
class QuoteListItem {
  const QuoteListItem({
    required this.id,
    required this.quoteNumber,
    required this.state,
    required this.createdAt,
    this.expiresAt,
    this.totals,
    this.submittedAt,
    this.submittedByName,
  });

  final String id;
  final String quoteNumber;
  final String state;
  final DateTime createdAt;
  final DateTime? expiresAt;
  final Money? totals;

  /// Populated by the `awaiting-my-approval` endpoint.
  final DateTime? submittedAt;
  final String? submittedByName;

  factory QuoteListItem.fromJson(Map<String, Object?> j) {
    final totals = j['totals'];
    final sb = j['submittedBy'];
    return QuoteListItem(
      id: j['id'] as String? ?? '',
      quoteNumber: j['quoteNumber'] as String? ?? '',
      state: j['state'] as String? ?? 'draft',
      createdAt:
          DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
      expiresAt: j['expiresAt'] is String
          ? DateTime.tryParse(j['expiresAt']! as String)
          : null,
      totals: totals is Map
          ? Money.fromJson(Map<String, Object?>.from(totals))
          : null,
      submittedAt: j['submittedAt'] is String
          ? DateTime.tryParse(j['submittedAt']! as String)
          : null,
      submittedByName: sb is Map ? sb['name'] as String? : null,
    );
  }
}

@immutable
class QuotesPage {
  const QuotesPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });

  final List<QuoteListItem> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory QuotesPage.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    return QuotesPage(
      items: items is List
          ? items
              .whereType<Map>()
              .map((m) => QuoteListItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      page: (j['page'] as num?)?.toInt() ?? 1,
      pageSize: (j['pageSize'] as num?)?.toInt() ?? 20,
      totalCount: (j['totalCount'] as num?)?.toInt() ?? 0,
    );
  }
}

// ============================================================
// Detail
// ============================================================

@immutable
class QuoteLine {
  const QuoteLine({
    required this.productId,
    required this.name,
    required this.qty,
    required this.unitPrice,
    required this.lineTotal,
  });

  final String productId;
  final String name;
  final int qty;
  final String unitPrice;
  final String lineTotal;

  factory QuoteLine.fromJson(Map<String, Object?> j) => QuoteLine(
        productId: j['productId'] as String? ?? '',
        name: j['name'] as String? ?? '',
        qty: (j['qty'] as num?)?.toInt() ?? 1,
        unitPrice: j['unitPrice']?.toString() ?? '0',
        lineTotal: j['lineTotal']?.toString() ?? '0',
      );
}

@immutable
class QuoteTotals {
  const QuoteTotals({
    required this.subtotal,
    required this.discount,
    required this.tax,
    required this.grandTotal,
    required this.currency,
  });

  final String subtotal;
  final String discount;
  final String tax;
  final String grandTotal;
  final String currency;

  factory QuoteTotals.fromJson(Map<String, Object?> j) => QuoteTotals(
        subtotal: j['subtotal']?.toString() ?? '0',
        discount: j['discount']?.toString() ?? '0',
        tax: j['tax']?.toString() ?? '0',
        grandTotal: j['grandTotal']?.toString() ?? '0',
        currency: j['currency'] as String? ?? '',
      );
}

@immutable
class QuoteDocumentRef {
  const QuoteDocumentRef({required this.locale, required this.url});
  final String locale;
  final String url;

  factory QuoteDocumentRef.fromJson(Map<String, Object?> j) => QuoteDocumentRef(
        locale: j['locale'] as String? ?? '',
        url: j['url'] as String? ?? '',
      );
}

@immutable
class QuoteVersion {
  const QuoteVersion({
    required this.versionId,
    required this.publishedAt,
    required this.lines,
    required this.totals,
    required this.terms,
    this.validUntil,
    this.documents = const [],
  });

  final String versionId;
  final DateTime publishedAt;
  final List<QuoteLine> lines;
  final QuoteTotals totals;
  final String terms;
  final DateTime? validUntil;
  final List<QuoteDocumentRef> documents;

  factory QuoteVersion.fromJson(Map<String, Object?> j) {
    final lines = j['lines'];
    final totals = j['totals'];
    final docs = j['documents'];
    return QuoteVersion(
      versionId: j['versionId'] as String? ?? '',
      publishedAt: DateTime.tryParse(j['publishedAt'] as String? ?? '') ??
          DateTime.now(),
      lines: lines is List
          ? lines
              .whereType<Map>()
              .map((m) => QuoteLine.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      totals: totals is Map
          ? QuoteTotals.fromJson(Map<String, Object?>.from(totals))
          : const QuoteTotals(
              subtotal: '0',
              discount: '0',
              tax: '0',
              grandTotal: '0',
              currency: '',
            ),
      terms: j['terms'] as String? ?? '',
      validUntil: j['validUntil'] is String
          ? DateTime.tryParse(j['validUntil']! as String)
          : null,
      documents: docs is List
          ? docs
              .whereType<Map>()
              .map((m) =>
                  QuoteDocumentRef.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

/// Action gating map from `GET /quotes/{id}`. The UI mirrors these
/// booleans — server is the source of truth (BR-2). Unknown future
/// keys parse as `false` so a stale client won't surface forbidden
/// actions.
@immutable
class QuoteActions {
  const QuoteActions({
    this.canSubmitAcceptance = false,
    this.canFinalizeAcceptance = false,
    this.canRejectAcceptance = false,
    this.canRequestRevision = false,
    this.canWithdraw = false,
    this.canSaveAsTemplate = false,
  });

  final bool canSubmitAcceptance;
  final bool canFinalizeAcceptance;
  final bool canRejectAcceptance;
  final bool canRequestRevision;
  final bool canWithdraw;
  final bool canSaveAsTemplate;

  factory QuoteActions.fromJson(Map<String, Object?> j) => QuoteActions(
        canSubmitAcceptance: j['canSubmitAcceptance'] == true,
        canFinalizeAcceptance: j['canFinalizeAcceptance'] == true,
        canRejectAcceptance: j['canRejectAcceptance'] == true,
        canRequestRevision: j['canRequestRevision'] == true,
        canWithdraw: j['canWithdraw'] == true,
        canSaveAsTemplate: j['canSaveAsTemplate'] == true,
      );
}

@immutable
class QuoteDetail {
  const QuoteDetail({
    required this.id,
    required this.quoteNumber,
    required this.state,
    required this.versions,
    required this.actions,
    this.submittedByName,
    this.submittedAt,
  });

  final String id;
  final String quoteNumber;
  final String state;
  final List<QuoteVersion> versions;
  final QuoteActions actions;
  final String? submittedByName;
  final DateTime? submittedAt;

  /// Most recent version — the UI defaults to this for pricing /
  /// documents / terms. Versions list still surfaces history.
  QuoteVersion? get latestVersion => versions.isEmpty ? null : versions.last;

  factory QuoteDetail.fromJson(Map<String, Object?> j) {
    final versions = j['versions'];
    final actions = j['actions'];
    final submittedBy = j['submittedBy'];
    return QuoteDetail(
      id: j['id'] as String? ?? '',
      quoteNumber: j['quoteNumber'] as String? ?? '',
      state: j['state'] as String? ?? 'draft',
      versions: versions is List
          ? versions
              .whereType<Map>()
              .map((m) => QuoteVersion.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      actions: actions is Map
          ? QuoteActions.fromJson(Map<String, Object?>.from(actions))
          : const QuoteActions(),
      submittedByName:
          submittedBy is Map ? submittedBy['name'] as String? : null,
      submittedAt: submittedBy is Map && submittedBy['submittedAt'] is String
          ? DateTime.tryParse(submittedBy['submittedAt']! as String)
          : null,
    );
  }
}

// ============================================================
// Create + action requests
// ============================================================

@immutable
class CreateQuoteFromCartRequest {
  const CreateQuoteFromCartRequest({
    required this.cartLines,
    required this.terms,
    this.expectedDeliveryDate,
    this.note,
  });

  final List<({String productId, int qty})> cartLines;
  final String terms;
  final DateTime? expectedDeliveryDate;
  final String? note;

  Map<String, Object?> toJson() => {
        'cartLines': [
          for (final l in cartLines) {'productId': l.productId, 'qty': l.qty},
        ],
        'terms': terms,
        if (expectedDeliveryDate != null)
          'expectedDeliveryDate': expectedDeliveryDate!.toIso8601String(),
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}

@immutable
class CreateQuoteFromProductRequest {
  const CreateQuoteFromProductRequest({
    required this.productId,
    required this.qty,
    required this.terms,
    this.expectedDeliveryDate,
    this.note,
  });

  final String productId;
  final int qty;
  final String terms;
  final DateTime? expectedDeliveryDate;
  final String? note;

  Map<String, Object?> toJson() => {
        'productId': productId,
        'qty': qty,
        'terms': terms,
        if (expectedDeliveryDate != null)
          'expectedDeliveryDate': expectedDeliveryDate!.toIso8601String(),
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}

@immutable
class CreateQuoteResult {
  const CreateQuoteResult({
    required this.id,
    required this.quoteNumber,
    required this.state,
    required this.createdAt,
  });

  final String id;
  final String quoteNumber;
  final String state;
  final DateTime createdAt;

  factory CreateQuoteResult.fromJson(Map<String, Object?> j) =>
      CreateQuoteResult(
        id: j['id'] as String? ?? '',
        quoteNumber: j['quoteNumber'] as String? ?? '',
        state: j['state'] as String? ?? 'draft',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

@immutable
class QuoteActionNoteRequest {
  const QuoteActionNoteRequest({this.note});
  final String? note;

  Map<String, Object?> toJson() => {
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}

@immutable
class SaveAsTemplateRequest {
  const SaveAsTemplateRequest({required this.templateName});
  final String templateName;

  Map<String, Object?> toJson() => {'templateName': templateName};
}

@immutable
class SaveAsTemplateResult {
  const SaveAsTemplateResult({required this.templateId});
  final String templateId;

  factory SaveAsTemplateResult.fromJson(Map<String, Object?> j) =>
      SaveAsTemplateResult(templateId: j['templateId'] as String? ?? '');
}
