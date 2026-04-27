// FR-002a — fail the build if any platform manifest registers a custom URL
// scheme. The customer app uses universal links + Android App Links only.
//
// Run via `dart run tool/lint/no_custom_url_scheme.dart`.

import 'dart:io';

void main(List<String> args) {
  final violations = <String>[];

  // Android — flag any <data android:scheme="..."> that isn't https.
  final manifest = File('android/app/src/main/AndroidManifest.xml');
  if (manifest.existsSync()) {
    final lines = manifest.readAsLinesSync();
    final schemeRe =
        RegExp(r'''android:scheme\s*=\s*"([^"]+)"''', caseSensitive: false);
    for (var i = 0; i < lines.length; i++) {
      for (final m in schemeRe.allMatches(lines[i])) {
        final v = m.group(1)!;
        if (v != 'https') {
          violations.add('android:${i + 1}: scheme="$v" (https only)');
        }
      }
    }
  }

  // iOS — flag any CFBundleURLSchemes entry in Info.plist.
  final plist = File('ios/Runner/Info.plist');
  if (plist.existsSync()) {
    final body = plist.readAsStringSync();
    if (body.contains('CFBundleURLSchemes') || body.contains('CFBundleURLTypes')) {
      violations.add('ios/Runner/Info.plist: CFBundleURLTypes/Schemes present');
    }
  }

  if (violations.isEmpty) {
    stdout.writeln('no_custom_url_scheme: 0 violations');
    return;
  }
  stderr.writeln('no_custom_url_scheme: ${violations.length} violation(s)');
  for (final v in violations) {
    stderr.writeln('  $v');
  }
  exitCode = 1;
}
