// FR-033 — fail the build if any code outside `lib/core/api/` reaches into
// `Uri.parse('http…')`, `dart:io HttpClient`, or constructs a `Dio` instance.
// Generated clients import the canonical Dio via the api module, so they
// don't trigger this check.
//
// Run via `dart run tool/lint/no_ad_hoc_http.dart` from `apps/customer_flutter/`.

import 'dart:io';

const _allowedRoots = {'lib/core/api', 'lib/generated', 'tool', 'test'};

void main(List<String> args) {
  final root = Directory('lib');
  if (!root.existsSync()) {
    stderr.writeln('lib/ not found — run from apps/customer_flutter/.');
    exitCode = 2;
    return;
  }

  final violations = <String>[];
  final httpUriRe = RegExp(r'''Uri\.parse\(\s*['"]http''');
  final ioHttpRe = RegExp(r'''HttpClient\(''');
  final dioCtorRe = RegExp(r'''\bDio\(''');

  void scan(File f) {
    final rel = f.path;
    if (_allowedRoots.any((p) => rel.startsWith(p))) return;
    if (!rel.endsWith('.dart')) return;
    final lines = f.readAsLinesSync();
    for (var i = 0; i < lines.length; i++) {
      final line = lines[i];
      if (httpUriRe.hasMatch(line) ||
          ioHttpRe.hasMatch(line) ||
          dioCtorRe.hasMatch(line)) {
        violations.add('$rel:${i + 1}: ${line.trim()}');
      }
    }
  }

  for (final entity in root.listSync(recursive: true)) {
    if (entity is File) scan(entity);
  }

  if (violations.isEmpty) {
    stdout.writeln('no_ad_hoc_http: 0 violations');
    return;
  }
  stderr.writeln('no_ad_hoc_http: ${violations.length} violation(s)');
  for (final v in violations) {
    stderr.writeln('  $v');
  }
  exitCode = 1;
}
