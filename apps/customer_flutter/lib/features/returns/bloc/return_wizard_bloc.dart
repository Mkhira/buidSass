import 'dart:async';

import 'package:bloc_concurrency/bloc_concurrency.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:image/image.dart' as img;
import 'package:uuid/uuid.dart';

import '../../orders/data/models/order_models.dart';
import '../../orders/data/orders_gateway.dart';
import '../data/models/return_models.dart';
import '../data/returns_gateway.dart';

// ============================================================
// Bloc events
// ============================================================

@immutable
sealed class ReturnWizardEvent {
  const ReturnWizardEvent();
}

/// Initial load — fetch return-eligibility for [orderId]. Also locks in
/// a single wizard-create idempotency-key for the lifetime of the bloc.
class ReturnWizardStarted extends ReturnWizardEvent {
  const ReturnWizardStarted(this.orderId);
  final String orderId;
}

/// Re-fetch eligibility (recovery from 409 stale-eligibility error).
class ReturnWizardEligibilityRefreshed extends ReturnWizardEvent {
  const ReturnWizardEligibilityRefreshed();
}

class ReturnWizardLineSelected extends ReturnWizardEvent {
  const ReturnWizardLineSelected({
    required this.productId,
    required this.selected,
  });
  final String productId;
  final bool selected;
}

class ReturnWizardLineReasonChanged extends ReturnWizardEvent {
  const ReturnWizardLineReasonChanged({
    required this.productId,
    required this.reason,
  });
  final String productId;
  final String reason;
}

class ReturnWizardLineNoteChanged extends ReturnWizardEvent {
  const ReturnWizardLineNoteChanged({
    required this.productId,
    required this.note,
  });
  final String productId;
  final String note;
}

class ReturnWizardPhotoAddRequested extends ReturnWizardEvent {
  const ReturnWizardPhotoAddRequested({
    required this.bytes,
    required this.filename,
  });

  /// Raw image bytes from the picker. The bloc downscales large files
  /// before upload per plan §Risk 2.
  final Uint8List bytes;
  final String filename;
}

class ReturnWizardPhotoRetried extends ReturnWizardEvent {
  const ReturnWizardPhotoRetried(this.clientPhotoKey);
  final String clientPhotoKey;
}

class ReturnWizardPhotoCancelled extends ReturnWizardEvent {
  const ReturnWizardPhotoCancelled(this.clientPhotoKey);
  final String clientPhotoKey;
}

class ReturnWizardRefundMethodChanged extends ReturnWizardEvent {
  const ReturnWizardRefundMethodChanged(this.method);

  /// `original_payment | bank_transfer | wallet` or null for default.
  final String? method;
}

class ReturnWizardSubmitted extends ReturnWizardEvent {
  const ReturnWizardSubmitted();
}

// ============================================================
// Photo tile model
// ============================================================

enum ReturnPhotoTileStatus { uploading, ready, failed }

@immutable
class ReturnPhotoTile {
  const ReturnPhotoTile({
    required this.clientPhotoKey,
    required this.bytes,
    required this.filename,
    required this.status,
    this.photoId,
    this.url,
    this.errorMessage,
  });

  /// UUID v4 generated on tile creation. Used as both the HTTP
  /// `Idempotency-Key` header AND the multipart form field
  /// `clientPhotoKey` so the server can dedupe by
  /// `(clientPhotoKey, checksum)`. Reused across upload retries for
  /// this tile, fresh per new tile (spec.md §S-6.2 + data-model.md).
  final String clientPhotoKey;

  /// Pre-downscaled bytes (≤ 2 MB after the wizard's downscale step).
  /// Kept around so retries don't have to re-pick the file.
  final Uint8List bytes;
  final String filename;
  final ReturnPhotoTileStatus status;
  final String? photoId;
  final String? url;
  final String? errorMessage;

  ReturnPhotoTile copyWith({
    ReturnPhotoTileStatus? status,
    String? photoId,
    String? url,
    String? errorMessage,
  }) =>
      ReturnPhotoTile(
        clientPhotoKey: clientPhotoKey,
        bytes: bytes,
        filename: filename,
        status: status ?? this.status,
        photoId: photoId ?? this.photoId,
        url: url ?? this.url,
        errorMessage: errorMessage ?? this.errorMessage,
      );
}

// ============================================================
// Per-line wizard form state
// ============================================================

@immutable
class ReturnWizardLineDraft {
  const ReturnWizardLineDraft({
    required this.productId,
    required this.qty,
    this.selected = false,
    this.reason,
    this.note,
  });

  final String productId;
  final int qty;
  final bool selected;
  final String? reason;
  final String? note;

  ReturnWizardLineDraft copyWith({
    bool? selected,
    String? reason,
    String? note,
  }) {
    return ReturnWizardLineDraft(
      productId: productId,
      qty: qty,
      selected: selected ?? this.selected,
      reason: reason ?? this.reason,
      note: note ?? this.note,
    );
  }
}

// ============================================================
// Bloc states
// ============================================================

@immutable
sealed class ReturnWizardState {
  const ReturnWizardState();
}

class ReturnWizardLoading extends ReturnWizardState {
  const ReturnWizardLoading();
}

/// No eligible lines for return — the wizard surfaces an "ineligible"
/// empty state and routes back. spec.md S-6.2 UI states.
class ReturnWizardIneligible extends ReturnWizardState {
  const ReturnWizardIneligible();
}

class ReturnWizardForm extends ReturnWizardState {
  const ReturnWizardForm({
    required this.eligibility,
    required this.lines,
    required this.photos,
    required this.refundMethod,
    this.validationError,
    this.eligibilityStale = false,
  });

  final ReturnEligibility eligibility;
  final List<ReturnWizardLineDraft> lines;
  final List<ReturnPhotoTile> photos;
  final String? refundMethod;

  /// Inline validation key for the screen to translate (e.g.
  /// `select_line`, `reason_required`).
  final String? validationError;

  /// Set after a 409 from submit; surfaces a "refresh" banner.
  final bool eligibilityStale;

  /// True when the wizard's invariants would let Submit run cleanly.
  /// Does NOT include validation errors — those gate on submit press.
  bool get canAttemptSubmit {
    if (!lines.any((l) => l.selected)) return false;
    for (final l in lines.where((l) => l.selected)) {
      if ((l.reason ?? '').isEmpty) return false;
    }
    return !photos.any((p) => p.status == ReturnPhotoTileStatus.uploading);
  }

  ReturnWizardForm copyWith({
    ReturnEligibility? eligibility,
    List<ReturnWizardLineDraft>? lines,
    List<ReturnPhotoTile>? photos,
    Object? refundMethod = _sentinel,
    Object? validationError = _sentinel,
    bool? eligibilityStale,
  }) {
    return ReturnWizardForm(
      eligibility: eligibility ?? this.eligibility,
      lines: lines ?? this.lines,
      photos: photos ?? this.photos,
      refundMethod: identical(refundMethod, _sentinel)
          ? this.refundMethod
          : refundMethod as String?,
      validationError: identical(validationError, _sentinel)
          ? this.validationError
          : validationError as String?,
      eligibilityStale: eligibilityStale ?? this.eligibilityStale,
    );
  }
}

const _sentinel = Object();

class ReturnWizardSubmitting extends ReturnWizardState {
  const ReturnWizardSubmitting(this.form);
  final ReturnWizardForm form;
}

class ReturnWizardSuccess extends ReturnWizardState {
  const ReturnWizardSuccess({
    required this.returnId,
    required this.returnNumber,
  });
  final String returnId;
  final String returnNumber;
}

class ReturnWizardFailure extends ReturnWizardState {
  const ReturnWizardFailure({
    required this.form,
    required this.reason,
    this.correlationId,
  });
  final ReturnWizardForm form;
  final String reason;
  final String? correlationId;
}

// ============================================================
// Bloc
// ============================================================

/// Photo downscale function — defaults to a real implementation backed
/// by the `image` package; tests inject a no-op.
typedef ReturnPhotoDownscaler = Uint8List Function(Uint8List input);

/// Default downscaler: encodes ≤ ~2 MB JPEG when the input is over
/// 10 MB. Plan §Risk 2.
Uint8List defaultReturnPhotoDownscaler(Uint8List input) {
  if (input.lengthInBytes <= 10 * 1024 * 1024) return input;
  final decoded = img.decodeImage(input);
  if (decoded == null) return input;
  // Resize down to 1600px on the long edge — that combined with JPEG
  // q80 lands well under 2 MB for typical phone photos.
  final resized = decoded.width >= decoded.height
      ? img.copyResize(decoded, width: 1600)
      : img.copyResize(decoded, height: 1600);
  return Uint8List.fromList(img.encodeJpg(resized, quality: 80));
}

/// Bloc for S-6.2 return wizard. Owns:
///
/// * The single wizard-create idempotency key (one per `started`,
///   reused across submit retries — BR-3).
/// * Per-tile `clientPhotoKey` UUIDs (one per tile, reused across
///   upload retries for that tile — BR-2 + data-model.md).
/// * Photo upload orchestration (parallel + retryable per tile).
/// * Eligibility refresh on 409 (spec.md S-6.2 UI state `error-409`).
class ReturnWizardBloc extends Bloc<ReturnWizardEvent, ReturnWizardState> {
  ReturnWizardBloc({
    required OrdersGateway ordersGateway,
    required ReturnsGateway returnsGateway,
    required this.orderId,
    Uuid? uuid,
    ReturnPhotoDownscaler? downscaler,
  })  : _orders = ordersGateway,
        _returns = returnsGateway,
        _uuid = uuid ?? const Uuid(),
        _downscale = downscaler ?? defaultReturnPhotoDownscaler,
        super(const ReturnWizardLoading()) {
    // One wizard-create key per bloc instance. Reused across submit
    // retries; cleared (with the bloc) on screen pop.
    _wizardCreateKey = _uuid.v4();
    on<ReturnWizardStarted>(_onStarted);
    on<ReturnWizardEligibilityRefreshed>(_onEligibilityRefreshed);
    on<ReturnWizardLineSelected>(_onLineSelected);
    on<ReturnWizardLineReasonChanged>(_onReasonChanged);
    on<ReturnWizardLineNoteChanged>(_onNoteChanged);
    on<ReturnWizardPhotoAddRequested>(_onPhotoAdd, transformer: concurrent());
    on<ReturnWizardPhotoRetried>(_onPhotoRetry, transformer: concurrent());
    on<ReturnWizardPhotoCancelled>(_onPhotoCancel);
    on<ReturnWizardRefundMethodChanged>(_onRefundMethodChanged);
    on<ReturnWizardSubmitted>(_onSubmitted);
  }

  final OrdersGateway _orders;
  final ReturnsGateway _returns;
  final String orderId;
  final Uuid _uuid;
  final ReturnPhotoDownscaler _downscale;

  late final String _wizardCreateKey;

  /// Exposed for tests + UI debug. The screen never displays it.
  @visibleForTesting
  String get wizardCreateKey => _wizardCreateKey;

  Future<void> _onStarted(
    ReturnWizardStarted event,
    Emitter<ReturnWizardState> emit,
  ) async {
    await _loadEligibility(emit);
  }

  Future<void> _onEligibilityRefreshed(
    ReturnWizardEligibilityRefreshed event,
    Emitter<ReturnWizardState> emit,
  ) async {
    await _loadEligibility(emit);
  }

  Future<void> _loadEligibility(Emitter<ReturnWizardState> emit) async {
    emit(const ReturnWizardLoading());
    try {
      final eligibility = await _orders.getReturnEligibility(orderId);
      if (!eligibility.anyEligible) {
        emit(const ReturnWizardIneligible());
        return;
      }
      final lines = eligibility.lines
          .where((l) => l.eligible)
          .map((l) => ReturnWizardLineDraft(productId: l.productId, qty: l.qty))
          .toList(growable: false);
      emit(ReturnWizardForm(
        eligibility: eligibility,
        lines: lines,
        photos: const [],
        refundMethod: null,
      ));
    } on Object catch (e) {
      // Eligibility load failure rolls into the Loading→Failure path
      // with an empty form so the screen can render a retry button.
      emit(ReturnWizardFailure(
        form: const ReturnWizardForm(
          eligibility: ReturnEligibility(lines: [], anyEligible: false),
          lines: [],
          photos: [],
          refundMethod: null,
        ),
        reason: e.toString(),
      ));
    }
  }

  void _onLineSelected(
    ReturnWizardLineSelected event,
    Emitter<ReturnWizardState> emit,
  ) {
    final form = _form();
    if (form == null) return;
    final lines = form.lines
        .map((l) => l.productId == event.productId
            ? l.copyWith(selected: event.selected)
            : l)
        .toList(growable: false);
    emit(form.copyWith(lines: lines, validationError: null));
  }

  void _onReasonChanged(
    ReturnWizardLineReasonChanged event,
    Emitter<ReturnWizardState> emit,
  ) {
    final form = _form();
    if (form == null) return;
    final lines = form.lines
        .map((l) => l.productId == event.productId
            ? l.copyWith(reason: event.reason)
            : l)
        .toList(growable: false);
    emit(form.copyWith(lines: lines, validationError: null));
  }

  void _onNoteChanged(
    ReturnWizardLineNoteChanged event,
    Emitter<ReturnWizardState> emit,
  ) {
    final form = _form();
    if (form == null) return;
    final lines = form.lines
        .map((l) =>
            l.productId == event.productId ? l.copyWith(note: event.note) : l)
        .toList(growable: false);
    emit(form.copyWith(lines: lines));
  }

  void _onRefundMethodChanged(
    ReturnWizardRefundMethodChanged event,
    Emitter<ReturnWizardState> emit,
  ) {
    final form = _form();
    if (form == null) return;
    emit(form.copyWith(refundMethod: event.method));
  }

  Future<void> _onPhotoAdd(
    ReturnWizardPhotoAddRequested event,
    Emitter<ReturnWizardState> emit,
  ) async {
    final form = _form();
    if (form == null) return;
    final key = _uuid.v4();
    // Downscale here so retries reuse the smaller buffer.
    final bytes = _downscale(event.bytes);
    final tile = ReturnPhotoTile(
      clientPhotoKey: key,
      bytes: bytes,
      filename: event.filename,
      status: ReturnPhotoTileStatus.uploading,
    );
    emit(form.copyWith(photos: [...form.photos, tile]));
    await _uploadTile(tile, emit);
  }

  Future<void> _onPhotoRetry(
    ReturnWizardPhotoRetried event,
    Emitter<ReturnWizardState> emit,
  ) async {
    final form = _form();
    if (form == null) return;
    // Retry/cancel race: a user can tap Retry on a tile that's just
    // been removed by another event. Silently drop the retry instead
    // of throwing (CodeRabbit feedback) — the cancel already won.
    ReturnPhotoTile? tile;
    for (final p in form.photos) {
      if (p.clientPhotoKey == event.clientPhotoKey) {
        tile = p;
        break;
      }
    }
    if (tile == null) return;
    final updated = tile.copyWith(
      status: ReturnPhotoTileStatus.uploading,
      errorMessage: '',
    );
    emit(form.copyWith(
      photos: _replacePhoto(form.photos, updated),
    ));
    await _uploadTile(updated, emit);
  }

  void _onPhotoCancel(
    ReturnWizardPhotoCancelled event,
    Emitter<ReturnWizardState> emit,
  ) {
    final form = _form();
    if (form == null) return;
    emit(form.copyWith(
      photos: form.photos
          .where((p) => p.clientPhotoKey != event.clientPhotoKey)
          .toList(growable: false),
    ));
  }

  Future<void> _uploadTile(
    ReturnPhotoTile tile,
    Emitter<ReturnWizardState> emit,
  ) async {
    try {
      final res = await _returns.uploadPhoto(
        bytes: tile.bytes,
        filename: tile.filename,
        // Per-tile key — same UUID across retries (BR-2). Server
        // returns the same photoId for `(clientPhotoKey, checksum)`.
        clientPhotoKey: tile.clientPhotoKey,
      );
      final form = _form();
      if (form == null) return;
      // The tile may have been cancelled while in-flight; preserve the
      // cancellation rather than re-adding it.
      if (!form.photos.any((p) => p.clientPhotoKey == tile.clientPhotoKey)) {
        return;
      }
      final updated = tile.copyWith(
        status: ReturnPhotoTileStatus.ready,
        photoId: res.photoId,
        url: res.url,
      );
      emit(form.copyWith(photos: _replacePhoto(form.photos, updated)));
    } on Object catch (e) {
      final form = _form();
      if (form == null) return;
      if (!form.photos.any((p) => p.clientPhotoKey == tile.clientPhotoKey)) {
        return;
      }
      final updated = tile.copyWith(
        status: ReturnPhotoTileStatus.failed,
        errorMessage: e.toString(),
      );
      emit(form.copyWith(photos: _replacePhoto(form.photos, updated)));
    }
  }

  Future<void> _onSubmitted(
    ReturnWizardSubmitted event,
    Emitter<ReturnWizardState> emit,
  ) async {
    final form = _form();
    if (form == null) return;
    final selected = form.lines.where((l) => l.selected).toList();
    if (selected.isEmpty) {
      emit(form.copyWith(validationError: 'select_line'));
      return;
    }
    if (selected.any((l) => (l.reason ?? '').isEmpty)) {
      emit(form.copyWith(validationError: 'reason_required'));
      return;
    }
    if (form.photos.any((p) => p.status == ReturnPhotoTileStatus.uploading)) {
      // Defensive — the UI also disables Submit while uploads are in
      // flight, but a quick double-tap could race. Hold off rather
      // than send half-uploaded photos.
      return;
    }

    emit(ReturnWizardSubmitting(form));
    try {
      final result = await _returns.create(
        orderId: orderId,
        request: CreateReturnRequest(
          lines: selected
              .map((l) => CreateReturnLine(
                    productId: l.productId,
                    qty: l.qty,
                    reason: l.reason!,
                    note: l.note,
                    photoIds: form.photos
                        .where((p) =>
                            p.status == ReturnPhotoTileStatus.ready &&
                            (p.photoId ?? '').isNotEmpty)
                        .map((p) => p.photoId!)
                        .toList(growable: false),
                  ))
              .toList(growable: false),
          preferredRefundMethod: form.refundMethod,
        ),
        // BR-3 — same key across submit retries. Locked in at bloc
        // construction; never regenerated within this wizard instance.
        idempotencyKey: _wizardCreateKey,
      );
      emit(ReturnWizardSuccess(
        returnId: result.id,
        returnNumber: result.returnNumber,
      ));
    } on Object catch (e) {
      final msg = e.toString();
      // Stale eligibility detection — both HTTP 409 (parsed by
      // ErrorMapper → ConflictFailure) and an "EligibilityExpired"
      // synthetic message from the stub. The screen shows a Refresh
      // banner that re-runs `started`.
      final isConflict = msg.contains('ConflictFailure') ||
          msg.contains('Conflict') ||
          msg.contains('409');
      emit(ReturnWizardFailure(
        form: form.copyWith(eligibilityStale: isConflict),
        reason: msg,
      ));
    }
  }

  ReturnWizardForm? _form() {
    final s = state;
    if (s is ReturnWizardForm) return s;
    if (s is ReturnWizardFailure) return s.form;
    return null;
  }

  List<ReturnPhotoTile> _replacePhoto(
    List<ReturnPhotoTile> photos,
    ReturnPhotoTile updated,
  ) {
    return photos
        .map((p) => p.clientPhotoKey == updated.clientPhotoKey ? updated : p)
        .toList(growable: false);
  }
}
