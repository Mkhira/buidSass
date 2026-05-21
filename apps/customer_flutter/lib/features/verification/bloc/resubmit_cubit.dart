import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/verification_models.dart';
import '../data/verification_gateway.dart';

// ============================================================
// State
// ============================================================

@immutable
sealed class ResubmitState {
  const ResubmitState();
}

class ResubmitLoading extends ResubmitState {
  const ResubmitLoading();
}

class ResubmitFailureLoad extends ResubmitState {
  const ResubmitFailureLoad({required this.reason});
  final String reason;
}

class ResubmitForm extends ResubmitState {
  const ResubmitForm({
    required this.detail,
    required this.editableFields,
    required this.values,
    required this.note,
    this.formError,
  });

  final VerificationDetail detail;

  /// Subset of fields the admin asked the customer to fix
  /// (BR-4 — only `requestedInfo[]` entries of kind=field).
  final List<VerificationRequestedInfo> editableFields;

  /// Pending edits scoped to those keys only. The original detail's
  /// values are read-only and shown for context.
  final Map<String, Object?> values;
  final String note;
  final String? formError;

  ResubmitForm copyWith({
    Map<String, Object?>? values,
    String? note,
    Object? formError = _sentinel,
  }) {
    return ResubmitForm(
      detail: detail,
      editableFields: editableFields,
      values: values ?? this.values,
      note: note ?? this.note,
      formError:
          identical(formError, _sentinel) ? this.formError : formError as String?,
    );
  }
}

class ResubmitSubmitting extends ResubmitState {
  const ResubmitSubmitting(this.form);
  final ResubmitForm form;
}

class ResubmitDone extends ResubmitState {
  const ResubmitDone(this.detail);
  final VerificationDetail detail;
}

const _sentinel = Object();

// ============================================================
// Cubit
// ============================================================

/// Cubit for S-7.4 resubmit. Scope is "fields the admin asked for" —
/// nothing else editable (BR-4). The Idempotency-Key is regenerated
/// on each cubit construction so re-entering the screen creates a
/// fresh resubmit intent (BR-4a).
class ResubmitCubit extends Cubit<ResubmitState> {
  ResubmitCubit({
    required VerificationGateway gateway,
    required String verificationId,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _verificationId = verificationId,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const ResubmitLoading());

  final VerificationGateway _gateway;
  final String _verificationId;
  final String _idempotencyKey;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  Future<void> load() async {
    emit(const ResubmitLoading());
    try {
      final detail = await _gateway.getById(_verificationId);
      final editable = detail.requestedInfo
          .where((ri) => ri.kind == 'field')
          .toList(growable: false);
      emit(ResubmitForm(
        detail: detail,
        editableFields: editable,
        values: const {},
        note: '',
      ));
    } on Object catch (e) {
      emit(ResubmitFailureLoad(reason: e.toString()));
    }
  }

  void fieldChanged(String key, Object? value) {
    final s = state;
    if (s is! ResubmitForm) return;
    if (!s.editableFields.any((f) => f.key == key)) {
      // Defensive: reject edits outside the requested-info scope.
      return;
    }
    final next = Map<String, Object?>.from(s.values);
    if (value == null || (value is String && value.isEmpty)) {
      next.remove(key);
    } else {
      next[key] = value;
    }
    emit(s.copyWith(values: next));
  }

  void noteChanged(String value) {
    final s = state;
    if (s is! ResubmitForm) return;
    emit(s.copyWith(note: value));
  }

  Future<void> submit() async {
    final s = state;
    if (s is! ResubmitForm) return;
    // Server validates the full set; client only checks that every
    // requested field has a value (since that's the whole point of
    // resubmit).
    final missing = s.editableFields.any((f) {
      final v = s.values[f.key];
      return v == null || (v is String && v.isEmpty);
    });
    if (missing) {
      emit(s.copyWith(formError: 'verificationSubmitErrorMissingRequired'));
      return;
    }
    emit(ResubmitSubmitting(s));
    try {
      final result = await _gateway.resubmit(
        verificationId: _verificationId,
        request: ResubmitVerificationRequest(
          fields: s.values,
          noteToAdmin: s.note.isEmpty ? null : s.note,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(ResubmitDone(result));
    } on Object catch (e) {
      emit(s.copyWith(formError: e.toString()));
    }
  }
}
