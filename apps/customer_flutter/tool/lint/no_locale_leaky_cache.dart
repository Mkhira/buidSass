// FR-009a — every repository / data source whose path matches an entry in
// `contracts/locale-aware-endpoints.md` MUST adopt the
// `i18n_aware_repository` mixin or key its cache by [LocaleBloc.state.locale].
//
// Run via `dart run tool/lint/no_locale_leaky_cache.dart`.

import 'dart:io';

const _registryPath =
    '../../specs/phase-1C/014-customer-app-shell/contracts/locale-aware-endpoints.md';

void main(List<String> args) {
  final registry = File(_registryPath);
  if (!registry.existsSync()) {
    stdout.writeln(
        'no_locale_leaky_cache: registry not found at $_registryPath — skipping.');
    return;
  }
  final body = registry.readAsStringSync();
  // Pull endpoint paths from the markdown table cells: `GET /v1/...`
  final endpointRe = RegExp(
    r'`(GET|POST|PUT|PATCH|DELETE)\s+(/v1/[^`]+)`',
  );
  final endpoints = endpointRe
      .allMatches(body)
      .map((m) => m.group(2)!.replaceFirst(RegExp(r'/:\w+'), '/'))
      .toSet();

  final dataDir = Directory('lib/features');
  if (!dataDir.existsSync()) {
    stdout.writeln('no_locale_leaky_cache: lib/features missing — skipping.');
    return;
  }

  final violations = <String>[];
  for (final entity in dataDir.listSync(recursive: true)) {
    if (entity is! File || !entity.path.endsWith('.dart')) continue;
    final segments = entity.path.split(Platform.pathSeparator);
    if (!segments.contains('data')) continue;
    final src = entity.readAsStringSync();
    final touchesEndpoint = endpoints.any(src.contains);
    if (!touchesEndpoint) continue;

    final adoptsMixin = src.contains('I18nAwareRepository') ||
        src.contains('with I18nAwareRepository');
    final keysByLocale =
        src.contains('LocaleBloc') || src.contains('locale.code');
    if (!adoptsMixin && !keysByLocale) {
      violations.add('${entity.path}: touches a locale-aware endpoint but '
          'does not adopt I18nAwareRepository or key cache by locale.');
    }
  }

  if (violations.isEmpty) {
    stdout.writeln('no_locale_leaky_cache: 0 violations');
    return;
  }
  stderr.writeln('no_locale_leaky_cache: ${violations.length} violation(s)');
  for (final v in violations) {
    stderr.writeln('  $v');
  }
  exitCode = 1;
}
