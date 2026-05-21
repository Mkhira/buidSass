import 'package:flutter/foundation.dart';

// ============================================================
// Verification — Phase 7 customer surface
// ============================================================
// Models parse the wire shapes spelled out in
// `specs/mobile/phase-7-trust-compliance/{spec.md,data-model.md}`. State
// values are wire strings (no client-side enum) so the server can add
// new states without a client release. Constitution Principle 24 — the
// verification state machine is explicit and kept separate from order /
// refund / payment states.

/// Wire-side verification states. The 5-value lifecycle from §S-7.1.
/// `info_requested` arrives when an admin needs the customer to fix
/// something; `expired` arrives when the case ages past its window.
const Set<String> kKnownVerificationStates = {
  'submitted',
  'info_requested',
  'approved',
  'rejected',
  'expired',
};

/// Wire schema field types — see plan.md "Dynamic form rendering".
/// Unknown values fall back to plain-text rendering per plan.md.
const Set<String> kKnownSchemaFieldTypes = {
  'text',
  'number',
  'enum',
  'date',
  'doc',
};

@immutable
class VerificationListItem {
  const VerificationListItem({
    required this.id,
    required this.kind,
    required this.state,
    required this.createdAt,
    this.expiresAt,
  });

  final String id;
  final String kind;
  final String state;
  final DateTime createdAt;
  final DateTime? expiresAt;

  factory VerificationListItem.fromJson(Map<String, Object?> j) =>
      VerificationListItem(
        id: j['id'] as String? ?? '',
        kind: j['kind'] as String? ?? '',
        state: j['state'] as String? ?? 'submitted',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        expiresAt: j['expiresAt'] is String
            ? DateTime.tryParse(j['expiresAt']! as String)
            : null,
      );
}

@immutable
class VerificationListPage {
  const VerificationListPage({required this.items});
  final List<VerificationListItem> items;

  factory VerificationListPage.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    return VerificationListPage(
      items: items is List
          ? items
              .whereType<Map>()
              .map((m) =>
                  VerificationListItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

/// `GET /verifications/active` — banner data. `state=none` when the
/// account has no active or in-progress case.
@immutable
class VerificationActive {
  const VerificationActive({
    required this.state,
    this.id,
    this.kind,
    this.expiresAt,
  });

  final String state;
  final String? id;
  final String? kind;
  final DateTime? expiresAt;

  bool get hasCase => state != 'none' && (id?.isNotEmpty ?? false);

  factory VerificationActive.fromJson(Map<String, Object?> j) =>
      VerificationActive(
        state: j['state'] as String? ?? 'none',
        id: j['id'] as String?,
        kind: j['kind'] as String?,
        expiresAt: j['expiresAt'] is String
            ? DateTime.tryParse(j['expiresAt']! as String)
            : null,
      );
}

// ============================================================
// Schema (dynamic form)
// ============================================================

@immutable
class SchemaFieldValidation {
  const SchemaFieldValidation({this.regex, this.minLength, this.maxLength});
  final String? regex;
  final int? minLength;
  final int? maxLength;

  factory SchemaFieldValidation.fromJson(Map<String, Object?> j) =>
      SchemaFieldValidation(
        regex: j['regex'] as String?,
        minLength: (j['minLength'] as num?)?.toInt(),
        maxLength: (j['maxLength'] as num?)?.toInt(),
      );
}

@immutable
class SchemaField {
  const SchemaField({
    required this.key,
    required this.label,
    required this.type,
    required this.required,
    this.options = const [],
    this.validation,
  });

  final String key;
  final String label;

  /// Wire enum: `text | number | enum | date | doc`. Unknown values
  /// render as a plain text input (plan.md defensive fallback).
  final String type;
  final bool required;
  final List<String> options;
  final SchemaFieldValidation? validation;

  factory SchemaField.fromJson(Map<String, Object?> j) {
    final options = j['options'];
    final validation = j['validation'];
    return SchemaField(
      key: j['key'] as String? ?? '',
      label: j['label'] as String? ?? '',
      type: j['type'] as String? ?? 'text',
      required: j['required'] == true,
      options:
          options is List ? options.whereType<String>().toList() : const [],
      validation: validation is Map
          ? SchemaFieldValidation.fromJson(
              Map<String, Object?>.from(validation))
          : null,
    );
  }
}

@immutable
class DocumentSlot {
  const DocumentSlot({
    required this.key,
    required this.label,
    required this.required,
  });

  final String key;
  final String label;
  final bool required;

  factory DocumentSlot.fromJson(Map<String, Object?> j) => DocumentSlot(
        key: j['key'] as String? ?? '',
        label: j['label'] as String? ?? '',
        required: j['required'] == true,
      );
}

@immutable
class VerificationSchema {
  const VerificationSchema({
    required this.kind,
    required this.fields,
    required this.documentSlots,
  });

  final String kind;
  final List<SchemaField> fields;
  final List<DocumentSlot> documentSlots;

  factory VerificationSchema.fromJson(Map<String, Object?> j) {
    final fields = j['fields'];
    final docs = j['documentSlots'];
    return VerificationSchema(
      kind: j['kind'] as String? ?? '',
      fields: fields is List
          ? fields
              .whereType<Map>()
              .map((m) => SchemaField.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      documentSlots: docs is List
          ? docs
              .whereType<Map>()
              .map((m) => DocumentSlot.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
    );
  }
}

// ============================================================
// Detail
// ============================================================

@immutable
class VerificationDocument {
  const VerificationDocument({
    required this.slotKey,
    required this.url,
    required this.uploadedAt,
  });

  final String slotKey;
  final String url;
  final DateTime uploadedAt;

  factory VerificationDocument.fromJson(Map<String, Object?> j) =>
      VerificationDocument(
        slotKey: j['slotKey'] as String? ?? '',
        url: j['url'] as String? ?? '',
        uploadedAt: DateTime.tryParse(j['uploadedAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

/// One entry in `requestedInfo[]`. `kind` is `doc | field`; `key` is
/// either a document slot key or a field key.
@immutable
class VerificationRequestedInfo {
  const VerificationRequestedInfo({
    required this.kind,
    required this.key,
    this.note,
  });

  final String kind;
  final String key;
  final String? note;

  factory VerificationRequestedInfo.fromJson(Map<String, Object?> j) =>
      VerificationRequestedInfo(
        kind: j['kind'] as String? ?? 'field',
        key: j['key'] as String? ?? '',
        note: j['note'] as String?,
      );
}

@immutable
class VerificationTimelineEvent {
  const VerificationTimelineEvent({
    required this.kind,
    required this.occurredAt,
    this.actor,
    this.note,
  });

  final String kind;
  final DateTime occurredAt;
  final String? actor;
  final String? note;

  factory VerificationTimelineEvent.fromJson(Map<String, Object?> j) =>
      VerificationTimelineEvent(
        kind: j['kind'] as String? ?? '',
        occurredAt: DateTime.tryParse(j['occurredAt'] as String? ?? '') ??
            DateTime.now(),
        actor: j['actor'] as String?,
        note: j['note'] as String?,
      );
}

@immutable
class VerificationDetail {
  const VerificationDetail({
    required this.id,
    required this.state,
    required this.kind,
    required this.createdAt,
    required this.fields,
    required this.documents,
    required this.requestedInfo,
    required this.timeline,
    this.priorVerificationId,
  });

  final String id;
  final String state;
  final String kind;
  final DateTime createdAt;
  final Map<String, Object?> fields;
  final List<VerificationDocument> documents;
  final List<VerificationRequestedInfo> requestedInfo;
  final List<VerificationTimelineEvent> timeline;
  final String? priorVerificationId;

  factory VerificationDetail.fromJson(Map<String, Object?> j) {
    final fields = j['fields'];
    final docs = j['documents'];
    final ri = j['requestedInfo'];
    final tl = j['timeline'];
    return VerificationDetail(
      id: j['id'] as String? ?? '',
      state: j['state'] as String? ?? 'submitted',
      kind: j['kind'] as String? ?? '',
      createdAt:
          DateTime.tryParse(j['createdAt'] as String? ?? '') ?? DateTime.now(),
      fields: fields is Map ? Map<String, Object?>.from(fields) : const {},
      documents: docs is List
          ? docs
              .whereType<Map>()
              .map((m) =>
                  VerificationDocument.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      requestedInfo: ri is List
          ? ri
              .whereType<Map>()
              .map((m) => VerificationRequestedInfo.fromJson(
                  Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      timeline: tl is List
          ? tl
              .whereType<Map>()
              .map((m) => VerificationTimelineEvent.fromJson(
                  Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      priorVerificationId: j['priorVerificationId'] as String?,
    );
  }
}

// ============================================================
// Mutations
// ============================================================

/// Body of `POST /verifications`. `fields` is server-driven, so we
/// pass the dynamic map through verbatim.
@immutable
class SubmitVerificationRequest {
  const SubmitVerificationRequest({
    required this.kind,
    required this.marketCode,
    required this.fields,
  });

  final String kind;
  final String marketCode;
  final Map<String, Object?> fields;

  Map<String, Object?> toJson() => {
        'kind': kind,
        'marketCode': marketCode,
        'fields': fields,
      };
}

@immutable
class SubmitVerificationResult {
  const SubmitVerificationResult({
    required this.id,
    required this.state,
    required this.createdAt,
  });

  final String id;
  final String state;
  final DateTime createdAt;

  factory SubmitVerificationResult.fromJson(Map<String, Object?> j) =>
      SubmitVerificationResult(
        id: j['id'] as String? ?? '',
        state: j['state'] as String? ?? 'submitted',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

@immutable
class ResubmitVerificationRequest {
  const ResubmitVerificationRequest({required this.fields, this.noteToAdmin});

  /// Only the keys present in `requestedInfo[]` are sent — the bloc
  /// scopes the diff before constructing the request.
  final Map<String, Object?> fields;
  final String? noteToAdmin;

  Map<String, Object?> toJson() => {
        'fields': fields,
        if (noteToAdmin != null && noteToAdmin!.isNotEmpty)
          'noteToAdmin': noteToAdmin,
      };
}

@immutable
class RenewVerificationRequest {
  const RenewVerificationRequest({
    required this.priorVerificationId,
    required this.marketCode,
  });

  final String priorVerificationId;
  final String marketCode;

  Map<String, Object?> toJson() => {
        'priorVerificationId': priorVerificationId,
        'marketCode': marketCode,
      };
}

@immutable
class DocumentUploadResult {
  const DocumentUploadResult({
    required this.slotKey,
    required this.url,
    required this.uploadedAt,
  });

  final String slotKey;
  final String url;
  final DateTime uploadedAt;

  factory DocumentUploadResult.fromJson(Map<String, Object?> j) =>
      DocumentUploadResult(
        slotKey: j['slotKey'] as String? ?? '',
        url: j['url'] as String? ?? '',
        uploadedAt: DateTime.tryParse(j['uploadedAt'] as String? ?? '') ??
            DateTime.now(),
      );
}
