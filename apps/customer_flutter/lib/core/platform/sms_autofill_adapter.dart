import 'dart:async';
import 'dart:io' show Platform;

import 'package:flutter/foundation.dart';
import 'package:sms_autofill/sms_autofill.dart';

/// SmsAutofillAdapter — Android-only wrapper around `sms_autofill`. iOS uses
/// the platform-native `oneTimeCode` content type on the text field; web has
/// no integration here. The adapter exposes a single [otpCodeStream] that
/// emits when the SMS Retriever API extracts a code from the OTP message.
class SmsAutofillAdapter {
  SmsAutofillAdapter();

  // ignore: close_sinks
  StreamController<String>? _controller;

  Stream<String> get otpCodeStream =>
      (_controller ??= StreamController<String>.broadcast()).stream;

  bool get isSupported {
    if (kIsWeb) return false;
    return Platform.isAndroid;
  }

  Future<String?> getAppSignature() async {
    if (!isSupported) return null;
    return SmsAutoFill().getAppSignature;
  }

  Future<void> listenForCode() async {
    if (!isSupported) return;
    await SmsAutoFill().listenForCode();
  }

  Future<void> unregister() async {
    if (!isSupported) return;
    await SmsAutoFill().unregisterListener();
  }

  Future<void> dispose() async {
    await unregister();
    await _controller?.close();
    _controller = null;
  }
}
