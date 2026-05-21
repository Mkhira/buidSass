import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/verification_models.dart';
import '../data/verification_gateway.dart';

// ============================================================
// Events
// ============================================================

@immutable
sealed class VerificationSubmitEvent {
  const VerificationSubmitEvent();
}

/// Mount — fetch the per-market schema and lock in a single wizard-
/// scoped Idempotency-Key for the lifetime of the bloc. The key is
/// regenerated if the user pops the screen and re-enters (a new bloc
/// instance is constructed).
class VerificationSubmitStarted extends VerificationSubmitEvent {
  const VerificationSubmitStarted({required this.marketCode});
  final String marketCode;
}

class VerificationSubmitFieldChanged extends VerificationSubmitEvent {
  const VerificationSubmitFieldChanged(
      {required this.key, required this.value});
  final String key;
  final Object? value;
}

class VerificationSubmitSubmitted extends VerificationSubmitEvent {
  const VerificationSubmitSubmitted();
}

// ============================================================
// States
// ============================================================

@immutable
sealed class VerificationSubmitState {
  const VerificationSubmitState();
}

class VerificationSubmitSchemaLoading extends VerificationSubmitState {
  const VerificationSubmitSchemaLoading();
}

class VerificationSubmitSchemaFailure extends VerificationSubmitState {
  const VerificationSubmitSchemaFailure({required this.reason});
  final String reason;
}

class VerificationSubmitForm extends VerificationSubmitState {
  const VerificationSubmitForm({
    required this.schema,
    required this.values,
    required this.fieldErrors,
    required this.marketCode,
    this.formError,
  });

  final VerificationSchema schema;
  final Map<String, Object?> values;

  /// Per-field client-side validation errors (localization keys, not
  /// raw copy). Empty when the form passes client validation.
  final Map<String, String> fieldErrors;
  final String marketCode;

  /// Server / network-level error not attributed to a single field.
  final String? formError;

  VerificationSubmitForm copyWith({
    Map<String, Object?>? values,
    Map<String, String>? fieldErrors,
    Object? formError = _sentinel,
  }) {
    return VerificationSubmitForm(
      schema: schema,
      values: values ?? this.values,
      fieldErrors: fieldErrors ?? this.fieldErrors,
      marketCode: marketCode,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class VerificationSubmitSubmitting extends VerificationSubmitState {
  const VerificationSubmitSubmitting(this.form);
  final VerificationSubmitForm form;
}

class VerificationSubmitDone extends VerificationSubmitState {
  const VerificationSubmitDone(this.result);
  final SubmitVerificationResult result;
}

const _sentinel = Object();

// ============================================================
// Bloc
// ============================================================

/// Bloc for S-7.2 verification submit. Renders a dynamic form from the
/// server-supplied schema (BR-1) and gates submission on client-side
/// required/regex validation. Final submit reuses one
/// `Idempotency-Key` across retries per BR-2 + data-model.md.
class VerificationSubmitBloc
    extends Bloc<VerificationSubmitEvent, VerificationSubmitState> {
  VerificationSubmitBloc({
    required VerificationGateway gateway,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const VerificationSubmitSchemaLoading()) {
    on<VerificationSubmitStarted>(_onStarted);
    on<VerificationSubmitFieldChanged>(_onFieldChanged);
    on<VerificationSubmitSubmitted>(_onSubmitted);
  }

  final VerificationGateway _gateway;
  final String _idempotencyKey;

  /// Last `marketCode` seen from `VerificationSubmitStarted`. Stored so
  /// the schema-failure screen can dispatch a fresh `Started` event to
  /// reload in-place instead of popping the route.
  String? _lastMarketCode;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  /// Exposed for the failure screen's retry CTA. Null when the bloc
  /// has never received a `Started` event.
  String? get lastMarketCode => _lastMarketCode;

  Future<void> _onStarted(
    VerificationSubmitStarted event,
    Emitter<VerificationSubmitState> emit,
  ) async {
    _lastMarketCode = event.marketCode;
    emit(const VerificationSubmitSchemaLoading());
    try {
      final schema = await _gateway.getSchema();
      emit(VerificationSubmitForm(
        schema: schema,
        values: const {},
        fieldErrors: const {},
        marketCode: event.marketCode,
      ));
    } on Object catch (_) {
      emit(const VerificationSubmitSchemaFailure(
        reason: 'verification.schema_failed',
      ));
    }
  }

  void _onFieldChanged(
    VerificationSubmitFieldChanged event,
    Emitter<VerificationSubmitState> emit,
  ) {
    final s = state;
    if (s is! VerificationSubmitForm) return;
    final nextValues = Map<String, Object?>.from(s.values);
    if (event.value == null ||
        (event.value is String && (event.value as String).isEmpty)) {
      nextValues.remove(event.key);
    } else {
      nextValues[event.key] = event.value;
    }
    // Clear the per-field error eagerly — re-validate on submit.
    final nextErrors = Map<String, String>.from(s.fieldErrors)
      ..remove(event.key);
    emit(s.copyWith(values: nextValues, fieldErrors: nextErrors));
  }

  Future<void> _onSubmitted(
    VerificationSubmitSubmitted event,
    Emitter<VerificationSubmitState> emit,
  ) async {
    final s = state;
    if (s is! VerificationSubmitForm) return;
    final errors = _validate(s.schema, s.values);
    if (errors.isNotEmpty) {
      emit(s.copyWith(
        fieldErrors: errors,
        formError: 'verificationSubmitErrorMissingRequired',
      ));
      return;
    }
    emit(VerificationSubmitSubmitting(s));
    try {
      final result = await _gateway.submit(
        request: SubmitVerificationRequest(
          kind: s.schema.kind,
          marketCode: s.marketCode,
          fields: s.values,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(VerificationSubmitDone(result));
    } on Object catch (e) {
      emit(s.copyWith(formError: e.toString()));
    }
  }

  /// Client-side validation. Required fields must be non-empty (string)
  /// or non-null (everything else). Regex / length checks apply when
  /// the schema supplied them. `doc` fields are NOT validated here —
  /// document upload is gated by the detail screen (S-7.3) after the
  /// case is created.
  Map<String, String> _validate(
    VerificationSchema schema,
    Map<String, Object?> values,
  ) {
    final errors = <String, String>{};
    for (final f in schema.fields) {
      if (f.type == 'doc') continue;
      final v = values[f.key];
      final isEmpty = v == null || (v is String && v.isEmpty);
      if (f.required && isEmpty) {
        errors[f.key] = 'verificationSubmitRequiredHint';
        continue;
      }
      if (!isEmpty && v is String) {
        final validation = f.validation;
        if (validation != null) {
          final regex = validation.regex;
          if (regex != null && regex.isNotEmpty) {
            try {
              if (!RegExp(regex).hasMatch(v)) {
                errors[f.key] = 'verificationSubmitErrorPattern';
                continue;
              }
            } on FormatException {
              // Ignore malformed server regex — server is the source
              // of truth on submit. Client validation is defensive.
            }
          }
          final min = validation.minLength;
          if (min != null && v.length < min) {
            errors[f.key] = 'verificationSubmitErrorPattern';
            continue;
          }
          final max = validation.maxLength;
          if (max != null && v.length > max) {
            errors[f.key] = 'verificationSubmitErrorPattern';
            continue;
          }
        }
      }
    }
    return errors;
  }
}
