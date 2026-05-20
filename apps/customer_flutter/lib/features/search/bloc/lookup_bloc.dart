import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../data/models/search_models.dart';
import '../data/search_gateway.dart';

// ===== State =====

@immutable
sealed class LookupState {
  const LookupState();
}

class LookupForm extends LookupState {
  const LookupForm();
}

class LookupScanning extends LookupState {
  const LookupScanning();
}

class LookupLooking extends LookupState {
  const LookupLooking();
}

class LookupMatched extends LookupState {
  const LookupMatched({required this.slug, required this.name, this.productId});
  final String slug;
  final String name;
  final String? productId;
}

class LookupNoMatch extends LookupState {
  const LookupNoMatch();
}

class LookupPermissionDenied extends LookupState {
  const LookupPermissionDenied();
}

class LookupFailure extends LookupState {
  const LookupFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

// ===== Events =====

@immutable
sealed class LookupEvent {
  const LookupEvent();
}

class LookupStarted extends LookupEvent {
  const LookupStarted();
}

class LookupScanRequested extends LookupEvent {
  const LookupScanRequested({required this.permissionGranted});

  /// Result of the camera permission probe performed by the screen
  /// (which owns `permission_handler` since the bloc must stay
  /// flutter-free for unit-test isolation).
  final bool permissionGranted;
}

class LookupScanCancelled extends LookupEvent {
  const LookupScanCancelled();
}

class LookupSubmitted extends LookupEvent {
  const LookupSubmitted({required this.value, required this.kind})
      : assert(kind == 'sku' || kind == 'barcode');
  final String value;
  final String kind; // sku | barcode
}

class LookupScanResult extends LookupEvent {
  const LookupScanResult(this.value);
  final String value;
}

// ===== Bloc =====

class LookupBloc extends Bloc<LookupEvent, LookupState> {
  LookupBloc({
    required SearchGateway gateway,
    required String Function() marketProvider,
  })  : _gateway = gateway,
        _market = marketProvider,
        super(const LookupForm()) {
    on<LookupStarted>((event, emit) => emit(const LookupForm()));
    on<LookupScanRequested>(_onScanRequested);
    on<LookupScanCancelled>((event, emit) => emit(const LookupForm()));
    on<LookupSubmitted>(_onSubmitted);
    on<LookupScanResult>(_onScanResult);
  }

  final SearchGateway _gateway;
  final String Function() _market;

  /// Tracks the last barcode + timestamp to debounce repeat reads (spec
  /// edge case "scanned twice within 1s").
  String? _lastScannedValue;
  DateTime? _lastScannedAt;

  Future<void> _onScanRequested(
      LookupScanRequested event, Emitter<LookupState> emit) async {
    if (!event.permissionGranted) {
      emit(const LookupPermissionDenied());
      return;
    }
    emit(const LookupScanning());
  }

  Future<void> _onSubmitted(
      LookupSubmitted event, Emitter<LookupState> emit) async {
    final v = event.value.trim();
    if (v.isEmpty) return;
    emit(const LookupLooking());
    try {
      final res = await _gateway.lookup(LookupRequest(
        sku: event.kind == 'sku' ? v : null,
        barcode: event.kind == 'barcode' ? v : null,
        marketCode: _market(),
      ));
      if (!res.matched || res.match?.slug == null) {
        emit(const LookupNoMatch());
        return;
      }
      emit(LookupMatched(
        productId: res.match!.productId,
        slug: res.match!.slug!,
        name: res.match!.name ?? '',
      ));
    } on Failure catch (f) {
      emit(LookupFailure(reason: f.code, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(LookupFailure(reason: e.toString()));
    }
  }

  Future<void> _onScanResult(
      LookupScanResult event, Emitter<LookupState> emit) async {
    final value = event.value.trim();
    if (value.isEmpty) return;
    final now = DateTime.now();
    if (_lastScannedValue == value &&
        _lastScannedAt != null &&
        now.difference(_lastScannedAt!) < const Duration(seconds: 1)) {
      // Duplicate read inside the 1-second cooldown — drop it (spec
      // edge case under S-3.4).
      return;
    }
    _lastScannedValue = value;
    _lastScannedAt = now;
    add(LookupSubmitted(value: value, kind: 'barcode'));
  }
}
