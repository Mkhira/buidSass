import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/verification/bloc/verification_submit_bloc.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

const _defaultSchema = VerificationSchema(
  kind: 'business_license',
  fields: [
    SchemaField(
      key: 'license',
      label: 'License',
      type: 'text',
      required: true,
      validation: SchemaFieldValidation(minLength: 3),
    ),
    SchemaField(
      key: 'specialty',
      label: 'Specialty',
      type: 'enum',
      required: true,
      options: ['general', 'ortho'],
    ),
    SchemaField(
      key: 'vat',
      label: 'VAT',
      type: 'text',
      required: false,
      validation: SchemaFieldValidation(regex: r'^\d+$'),
    ),
  ],
  documentSlots: [
    DocumentSlot(key: 'id_front', label: 'ID', required: true),
  ],
);

class _FakeGateway implements VerificationGateway {
  _FakeGateway({
    this.throwOnSchema = false,
    this.throwOnSubmit = false,
  });

  final VerificationSchema schema = _defaultSchema;
  final bool throwOnSchema;
  final bool throwOnSubmit;

  final List<SubmitVerificationRequest> submits = [];
  final List<String> idempotencyKeys = [];

  @override
  Future<VerificationSchema> getSchema() async {
    if (throwOnSchema) throw Exception('schema down');
    return schema;
  }

  @override
  Future<SubmitVerificationResult> submit({
    required SubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    submits.add(request);
    idempotencyKeys.add(idempotencyKey);
    if (throwOnSubmit) throw Exception('submit failed');
    return SubmitVerificationResult(
      id: 'v-new',
      state: 'submitted',
      createdAt: DateTime.utc(2026, 5, 20),
    );
  }

  // -- unused in this bloc --

  @override
  Future<VerificationActive> getActive() async => throw UnimplementedError();

  @override
  Future<VerificationListPage> list() async => throw UnimplementedError();

  @override
  Future<VerificationDetail> getById(String id) => throw UnimplementedError();

  @override
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  }) =>
      throw UnimplementedError();

  @override
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

void main() {
  group('schema load', () {
    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'success → form with empty values',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) => b.add(const VerificationSubmitStarted(marketCode: 'SA')),
      expect: () => [
        isA<VerificationSubmitSchemaLoading>(),
        isA<VerificationSubmitForm>().having(
          (s) => s.schema.fields.length,
          'fields',
          3,
        ),
      ],
    );

    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'failure → schema failure',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(throwOnSchema: true),
      ),
      act: (b) => b.add(const VerificationSubmitStarted(marketCode: 'SA')),
      expect: () => [
        isA<VerificationSubmitSchemaLoading>(),
        isA<VerificationSubmitSchemaFailure>(),
      ],
    );
  });

  group('field changes', () {
    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'records value + clears prior error',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(
          key: 'license',
          value: 'AB-123',
        ));
      },
      skip: 2, // skip loading + initial form
      expect: () => [
        isA<VerificationSubmitForm>()
            .having((s) => s.values['license'], 'license', 'AB-123'),
      ],
    );

    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'empty value removes key from values',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(
          key: 'license',
          value: 'AB-123',
        ));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(key: 'license', value: ''));
      },
      skip: 3,
      expect: () => [
        isA<VerificationSubmitForm>()
            .having((s) => s.values.containsKey('license'), 'cleared', isFalse),
      ],
    );
  });

  group('submit', () {
    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'missing required fields → field errors, no submit fired',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitSubmitted());
      },
      skip: 2,
      expect: () => [
        isA<VerificationSubmitForm>()
            .having((s) => s.fieldErrors['license'], 'license error', isNotNull)
            .having(
              (s) => s.fieldErrors['specialty'],
              'specialty error',
              isNotNull,
            )
            .having((s) => s.formError, 'formError', isNotNull),
      ],
    );

    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'regex violation surfaces as field error',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(
          key: 'license',
          value: 'AB-123',
        ));
        b.add(const VerificationSubmitFieldChanged(
          key: 'specialty',
          value: 'general',
        ));
        // optional field with regex — should fail
        b.add(const VerificationSubmitFieldChanged(key: 'vat', value: 'abc'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitSubmitted());
      },
      skip: 5,
      expect: () => [
        isA<VerificationSubmitForm>()
            .having((s) => s.fieldErrors['vat'], 'vat error', isNotNull),
      ],
    );

    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'happy path: submit with Idempotency-Key → done',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(),
        idempotencyKeyFactory: () => 'wizard-key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(
          key: 'license',
          value: 'AB-123',
        ));
        b.add(const VerificationSubmitFieldChanged(
          key: 'specialty',
          value: 'general',
        ));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitSubmitted());
      },
      skip: 4,
      expect: () => [
        isA<VerificationSubmitSubmitting>(),
        isA<VerificationSubmitDone>()
            .having((s) => s.result.id, 'id', 'v-new'),
      ],
      verify: (b) {
        expect(b.idempotencyKey, 'wizard-key-1');
      },
    );

    blocTest<VerificationSubmitBloc, VerificationSubmitState>(
      'gateway throws → form keeps values + carries formError',
      build: () => VerificationSubmitBloc(
        gateway: _FakeGateway(throwOnSubmit: true),
        idempotencyKeyFactory: () => 'key-1',
      ),
      act: (b) async {
        b.add(const VerificationSubmitStarted(marketCode: 'SA'));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitFieldChanged(
          key: 'license',
          value: 'AB-123',
        ));
        b.add(const VerificationSubmitFieldChanged(
          key: 'specialty',
          value: 'general',
        ));
        await Future<void>.delayed(Duration.zero);
        b.add(const VerificationSubmitSubmitted());
      },
      skip: 4,
      expect: () => [
        isA<VerificationSubmitSubmitting>(),
        isA<VerificationSubmitForm>()
            .having((s) => s.formError, 'formError', isNotNull)
            .having((s) => s.values['license'], 'license preserved', 'AB-123'),
      ],
    );
  });
}
