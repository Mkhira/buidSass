import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/verification_models.dart';
import '../data/verification_gateway.dart';

// ============================================================
// Events
// ============================================================

@immutable
sealed class VerificationDetailEvent {
  const VerificationDetailEvent();
}

class VerificationDetailStarted extends VerificationDetailEvent {
  const VerificationDetailStarted();
}

class VerificationDetailRefreshed extends VerificationDetailEvent {
  const VerificationDetailRefreshed();
}

/// Upload a document for [slotKey]. The bloc queues uploads with
/// bounded parallelism (≤2 concurrent, S-7.3 AC) so multi-slot
/// submissions don't saturate the wire while still feeling parallel.
class VerificationDocumentUploadRequested extends VerificationDetailEvent {
  const VerificationDocumentUploadRequested({
    required this.slotKey,
    required this.bytes,
    required this.filename,
  });

  final String slotKey;
  final Uint8List bytes;
  final String filename;
}

// ============================================================
// State
// ============================================================

enum SlotUploadStatus { idle, uploading, ready, failed }

@immutable
class SlotUploadState {
  const SlotUploadState({
    required this.status,
    this.errorMessage,
  });

  final SlotUploadStatus status;
  final String? errorMessage;

  static const idle = SlotUploadState(status: SlotUploadStatus.idle);
}

@immutable
sealed class VerificationDetailState {
  const VerificationDetailState();
}

class VerificationDetailLoading extends VerificationDetailState {
  const VerificationDetailLoading();
}

class VerificationDetailFailure extends VerificationDetailState {
  const VerificationDetailFailure({required this.reason});
  final String reason;
}

class VerificationDetailLoaded extends VerificationDetailState {
  const VerificationDetailLoaded({
    required this.detail,
    required this.uploads,
  });

  final VerificationDetail detail;

  /// Per-slot upload status — keyed by `slotKey` from the schema /
  /// requested-info. Documents already uploaded server-side surface in
  /// `detail.documents`; this map tracks in-flight client uploads.
  final Map<String, SlotUploadState> uploads;

  /// True when every entry in `detail.requestedInfo` (kind=doc) has
  /// either a server document or a ready upload, AND every requested
  /// field has a non-empty value. The Resubmit CTA gates on this
  /// (S-7.3 AC).
  bool get resubmitReady {
    if (detail.requestedInfo.isEmpty) return false;
    final serverSlots =
        detail.documents.map((d) => d.slotKey).toSet();
    for (final ri in detail.requestedInfo) {
      if (ri.kind == 'doc') {
        final hasServer = serverSlots.contains(ri.key);
        final localReady = uploads[ri.key]?.status == SlotUploadStatus.ready;
        if (!hasServer && !localReady) return false;
      } else if (ri.kind == 'field') {
        final v = detail.fields[ri.key];
        final isEmpty = v == null || (v is String && v.isEmpty);
        if (isEmpty) return false;
      }
    }
    return true;
  }

  VerificationDetailLoaded copyWith({
    VerificationDetail? detail,
    Map<String, SlotUploadState>? uploads,
  }) {
    return VerificationDetailLoaded(
      detail: detail ?? this.detail,
      uploads: uploads ?? this.uploads,
    );
  }
}

// ============================================================
// Bloc
// ============================================================

/// Bloc for S-7.3 verification detail + document upload. Uploads are
/// gated through a bounded semaphore (max 2 concurrent) so multi-doc
/// submissions feel parallel without saturating mobile bandwidth.
class VerificationDetailBloc
    extends Bloc<VerificationDetailEvent, VerificationDetailState> {
  VerificationDetailBloc({
    required VerificationGateway gateway,
    required String verificationId,
    int maxConcurrentUploads = 2,
  })  : _gateway = gateway,
        _verificationId = verificationId,
        _semaphore = _Semaphore(maxConcurrentUploads),
        super(const VerificationDetailLoading()) {
    on<VerificationDetailStarted>(_load);
    on<VerificationDetailRefreshed>(_load);
    on<VerificationDocumentUploadRequested>(_onUpload);
  }

  final VerificationGateway _gateway;
  final String _verificationId;
  final _Semaphore _semaphore;

  Future<void> _load(
    VerificationDetailEvent event,
    Emitter<VerificationDetailState> emit,
  ) async {
    // Preserve any in-flight upload states across a refresh — the user
    // shouldn't see "uploading" reset to idle just because they pulled.
    final priorUploads = state is VerificationDetailLoaded
        ? (state as VerificationDetailLoaded).uploads
        : const <String, SlotUploadState>{};
    if (state is! VerificationDetailLoaded) {
      emit(const VerificationDetailLoading());
    }
    try {
      final detail = await _gateway.getById(_verificationId);
      emit(VerificationDetailLoaded(
        detail: detail,
        uploads: _reconcileUploads(detail, priorUploads),
      ));
    } on Object catch (e) {
      emit(VerificationDetailFailure(reason: e.toString()));
    }
  }

  Future<void> _onUpload(
    VerificationDocumentUploadRequested event,
    Emitter<VerificationDetailState> emit,
  ) async {
    final s = state;
    if (s is! VerificationDetailLoaded) return;

    // Mark slot as uploading immediately so the UI can show progress.
    emit(s.copyWith(uploads: {
      ...s.uploads,
      event.slotKey:
          const SlotUploadState(status: SlotUploadStatus.uploading),
    }));

    final ticket = await _semaphore.acquire();
    try {
      final result = await _gateway.uploadDocument(
        verificationId: _verificationId,
        slotKey: event.slotKey,
        bytes: event.bytes,
        filename: event.filename,
      );
      final current = state;
      if (current is VerificationDetailLoaded) {
        // Server returns the new document — splice it into the detail
        // so the UI flips from upload-pending to uploaded-and-visible
        // without waiting for the next refresh.
        final nextDocs = [
          ...current.detail.documents.where((d) => d.slotKey != event.slotKey),
          VerificationDocument(
            slotKey: result.slotKey,
            url: result.url,
            uploadedAt: result.uploadedAt,
          ),
        ];
        emit(current.copyWith(
          detail: VerificationDetail(
            id: current.detail.id,
            state: current.detail.state,
            kind: current.detail.kind,
            createdAt: current.detail.createdAt,
            fields: current.detail.fields,
            documents: nextDocs,
            requestedInfo: current.detail.requestedInfo,
            timeline: current.detail.timeline,
            priorVerificationId: current.detail.priorVerificationId,
          ),
          uploads: {
            ...current.uploads,
            event.slotKey:
                const SlotUploadState(status: SlotUploadStatus.ready),
          },
        ));
      }
    } on Object catch (e) {
      final current = state;
      if (current is VerificationDetailLoaded) {
        emit(current.copyWith(uploads: {
          ...current.uploads,
          event.slotKey: SlotUploadState(
            status: SlotUploadStatus.failed,
            errorMessage: e.toString(),
          ),
        }));
      }
    } finally {
      ticket.release();
    }
  }

  /// On reload, drop any upload state for slots the server now reports
  /// as uploaded (so the per-slot UI moves from "uploading" to the
  /// final document tile cleanly).
  Map<String, SlotUploadState> _reconcileUploads(
    VerificationDetail detail,
    Map<String, SlotUploadState> prior,
  ) {
    if (prior.isEmpty) return const {};
    final serverSlots = detail.documents.map((d) => d.slotKey).toSet();
    return {
      for (final e in prior.entries)
        if (!serverSlots.contains(e.key)) e.key: e.value,
    };
  }

  @override
  Future<void> close() async {
    _semaphore.dispose();
    await super.close();
  }
}

// ============================================================
// Bounded-concurrency helper
// ============================================================

/// Counting semaphore. Bounds parallel doc uploads to N (S-7.3 AC).
class _Semaphore {
  _Semaphore(this._max) : _available = _max;

  final int _max;
  int _available;
  final List<Completer<_Ticket>> _waiters = [];

  Future<_Ticket> acquire() {
    if (_available > 0) {
      _available--;
      return Future.value(_Ticket(this));
    }
    final c = Completer<_Ticket>();
    _waiters.add(c);
    return c.future;
  }

  void _release() {
    if (_waiters.isNotEmpty) {
      final next = _waiters.removeAt(0);
      next.complete(_Ticket(this));
    } else if (_available < _max) {
      _available++;
    }
  }

  void dispose() {
    for (final c in _waiters) {
      if (!c.isCompleted) c.completeError(StateError('bloc closed'));
    }
    _waiters.clear();
  }
}

class _Ticket {
  _Ticket(this._sem);
  final _Semaphore _sem;
  bool _released = false;
  void release() {
    if (_released) return;
    _released = true;
    _sem._release();
  }
}
