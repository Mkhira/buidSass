import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';
import 'checkout_drift.dart';

@immutable
sealed class CheckoutAddressState {
  const CheckoutAddressState();
}

class CheckoutAddressForm extends CheckoutAddressState {
  const CheckoutAddressForm({this.initial});
  final CheckoutAddressDto? initial;
}

class CheckoutAddressSubmitting extends CheckoutAddressState {
  const CheckoutAddressSubmitting();
}

class CheckoutAddressSubmitted extends CheckoutAddressState {
  const CheckoutAddressSubmitted(this.summary);
  final CheckoutSummary summary;
}

class CheckoutAddressConflict extends CheckoutAddressState {
  const CheckoutAddressConflict(this.conflict);
  final CheckoutConflict conflict;
}

class CheckoutAddressFailure extends CheckoutAddressState {
  const CheckoutAddressFailure({required this.reason, this.fields = const {}});
  final String reason;
  final Map<String, String> fields;
}

@immutable
sealed class CheckoutAddressEvent {
  const CheckoutAddressEvent();
}

class AddressFormHydrated extends CheckoutAddressEvent {
  const AddressFormHydrated(this.address);
  final CheckoutAddressDto? address;
}

class AddressSubmitted extends CheckoutAddressEvent {
  const AddressSubmitted(this.address);
  final CheckoutAddressDto address;
}

class AddressDriftResolved extends CheckoutAddressEvent {
  const AddressDriftResolved({required this.address});
  final CheckoutAddressDto address;
}

class CheckoutAddressBloc
    extends Bloc<CheckoutAddressEvent, CheckoutAddressState>
    with CheckoutDriftAware {
  CheckoutAddressBloc({
    required CheckoutGateway gateway,
    required this.sessionId,
    CheckoutAddressDto? initial,
  })  : _gateway = gateway,
        super(CheckoutAddressForm(initial: initial)) {
    on<AddressFormHydrated>(
        (e, emit) => emit(CheckoutAddressForm(initial: e.address)));
    on<AddressSubmitted>(_onSubmit);
    on<AddressDriftResolved>((e, emit) => _patch(e.address, emit));
  }

  final CheckoutGateway _gateway;
  final String sessionId;

  Future<void> _onSubmit(
    AddressSubmitted event,
    Emitter<CheckoutAddressState> emit,
  ) async {
    final fields = _validate(event.address);
    if (fields.isNotEmpty) {
      emit(CheckoutAddressFailure(reason: 'validation', fields: fields));
      return;
    }
    await _patch(event.address, emit);
  }

  Future<void> _patch(
    CheckoutAddressDto address,
    Emitter<CheckoutAddressState> emit,
  ) async {
    emit(const CheckoutAddressSubmitting());
    try {
      final summary = await _gateway.patchAddress(
        sessionId: sessionId,
        address: address,
      );
      emit(CheckoutAddressSubmitted(summary));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutAddressConflict(driftFrom(e)));
    } on Object catch (e) {
      emit(CheckoutAddressFailure(reason: e.toString()));
    }
  }

  /// Client-side guardrails. Wire format validation is server-side; this
  /// only catches obviously-empty fields and phone shape so the user
  /// gets immediate feedback before the round-trip.
  Map<String, String> _validate(CheckoutAddressDto a) {
    final out = <String, String>{};
    if (a.name.trim().isEmpty) out['name'] = 'required';
    if (a.city.trim().isEmpty) out['city'] = 'required';
    if (a.street.trim().isEmpty) out['street'] = 'required';
    final phone = a.phone.trim();
    if (phone.isEmpty) {
      out['phone'] = 'required';
    } else if (!RegExp(r'^\+?[0-9]{6,15}$').hasMatch(phone)) {
      // E.164 shape per spec.md AC. Server normalizes the canonical
      // form; this is just a soft guardrail.
      out['phone'] = 'invalid';
    }
    return out;
  }
}
