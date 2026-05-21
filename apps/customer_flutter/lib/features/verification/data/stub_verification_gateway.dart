import 'dart:typed_data';

import 'models/verification_models.dart';
import 'verification_gateway.dart';

/// Deterministic in-memory [VerificationGateway] for offline dev.
///
/// Returns a single mocked KSA business-license schema, a 2-row history
/// with one approved and one `info_requested` case, and stable doc
/// upload ids derived from the slot key.
class StubVerificationGateway implements VerificationGateway {
  StubVerificationGateway({DateTime? now}) : _now = now ?? _seedNow;

  static final DateTime _seedNow = DateTime.utc(2026, 5, 20);

  final DateTime _now;

  // Mutable state so submit/upload/resubmit reflect in subsequent reads.
  final Map<String, VerificationDetail> _details = {};

  @override
  Future<VerificationListPage> list() async {
    return VerificationListPage(items: [
      VerificationListItem(
        id: 'v-active',
        kind: 'business_license',
        state: 'info_requested',
        createdAt: _now.subtract(const Duration(days: 1)),
      ),
      VerificationListItem(
        id: 'v-prior',
        kind: 'business_license',
        state: 'approved',
        createdAt: _now.subtract(const Duration(days: 365)),
        expiresAt: _now.add(const Duration(days: 14)),
      ),
    ]);
  }

  @override
  Future<VerificationActive> getActive() async {
    return VerificationActive(
      id: 'v-active',
      kind: 'business_license',
      state: 'info_requested',
      expiresAt: _now.add(const Duration(days: 14)),
    );
  }

  @override
  Future<VerificationSchema> getSchema() async {
    return const VerificationSchema(
      kind: 'business_license',
      fields: [
        SchemaField(
          key: 'businessLicense',
          label: 'Business license number',
          type: 'text',
          required: true,
          validation: SchemaFieldValidation(minLength: 3),
        ),
        SchemaField(
          key: 'vat',
          label: 'VAT number',
          type: 'text',
          required: false,
        ),
        SchemaField(
          key: 'specialty',
          label: 'Specialty',
          type: 'enum',
          required: true,
          options: ['general', 'orthodontics', 'periodontics', 'oral_surgery'],
        ),
        SchemaField(
          key: 'graduationDate',
          label: 'Graduation date',
          type: 'date',
          required: false,
        ),
      ],
      documentSlots: [
        DocumentSlot(key: 'id_front', label: 'ID — front', required: true),
        DocumentSlot(key: 'id_back', label: 'ID — back', required: true),
        DocumentSlot(
          key: 'license',
          label: 'Professional license',
          required: true,
        ),
      ],
    );
  }

  @override
  Future<VerificationDetail> getById(String id) async {
    final cached = _details[id];
    if (cached != null) return cached;
    final isInfoRequested = id == 'v-active';
    final placed = _now.subtract(const Duration(days: 2));
    final detail = VerificationDetail(
      id: id,
      state: isInfoRequested ? 'info_requested' : 'approved',
      kind: 'business_license',
      createdAt: placed,
      fields: const {
        'businessLicense': 'AB-12345',
        'specialty': 'general',
      },
      documents: [
        VerificationDocument(
          slotKey: 'id_front',
          url: 'https://stub.example/v/$id/id_front.jpg',
          uploadedAt: placed,
        ),
      ],
      requestedInfo: isInfoRequested
          ? const [
              VerificationRequestedInfo(
                kind: 'doc',
                key: 'id_back',
                note: 'Please re-upload the back of the ID — image is blurry.',
              ),
              VerificationRequestedInfo(
                kind: 'field',
                key: 'vat',
                note: 'Provide a VAT number if registered.',
              ),
            ]
          : const [],
      timeline: [
        VerificationTimelineEvent(
          kind: 'submitted',
          occurredAt: placed,
          actor: 'customer',
        ),
        if (isInfoRequested)
          VerificationTimelineEvent(
            kind: 'info_requested',
            occurredAt: placed.add(const Duration(hours: 8)),
            actor: 'admin',
            note: 'Two items need attention.',
          ),
      ],
    );
    _details[id] = detail;
    return detail;
  }

  @override
  Future<SubmitVerificationResult> submit({
    required SubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    final id = 'v-${idempotencyKey.substring(0, 8)}';
    return SubmitVerificationResult(
      id: id,
      state: 'submitted',
      createdAt: _now,
    );
  }

  @override
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  }) async {
    return DocumentUploadResult(
      slotKey: slotKey,
      url: 'https://stub.example/v/$verificationId/$slotKey.jpg',
      uploadedAt: _now,
    );
  }

  @override
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    final existing = await getById(verificationId);
    final next = VerificationDetail(
      id: existing.id,
      state: 'submitted',
      kind: existing.kind,
      createdAt: existing.createdAt,
      fields: {...existing.fields, ...request.fields},
      documents: existing.documents,
      requestedInfo: const [],
      timeline: [
        ...existing.timeline,
        VerificationTimelineEvent(
          kind: 'submitted',
          occurredAt: _now,
          actor: 'customer',
          note: request.noteToAdmin,
        ),
      ],
    );
    _details[verificationId] = next;
    return next;
  }

  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) async {
    return SubmitVerificationResult(
      id: 'v-renew-${idempotencyKey.substring(0, 8)}',
      state: 'submitted',
      createdAt: _now,
    );
  }
}
