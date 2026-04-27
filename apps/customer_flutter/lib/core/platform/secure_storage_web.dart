// secure_storage_web — placeholder for the web-specific encrypted-localStorage
// adapter. `flutter_secure_storage` 9.x already implements a WebCrypto-backed
// `localStorage` adapter on web, so this file currently exposes an opt-in
// configuration helper. If a future spec needs a custom AES-GCM wrap that
// differs from the package's default it lands here.
//
// The platform-specific options are surfaced so the composition root can pass
// them to [FlutterSecureStorage] without scattering platform-detection logic
// across feature modules.

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureStoragePlatformOptions {
  const SecureStoragePlatformOptions();

  AndroidOptions get android => const AndroidOptions(
        encryptedSharedPreferences: true,
      );

  IOSOptions get iOS => const IOSOptions(
        accessibility: KeychainAccessibility.first_unlock_this_device,
      );

  WebOptions get web => const WebOptions(
        dbName: 'customer_flutter_secure_storage',
        publicKey: 'customer_flutter_pubkey',
      );

  FlutterSecureStorage build() => FlutterSecureStorage(
        aOptions: android,
        iOptions: iOS,
        webOptions: web,
      );
}
