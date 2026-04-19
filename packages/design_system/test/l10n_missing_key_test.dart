import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('l10n parity check fails when EN key is missing in AR', () async {
    final temp = await Directory.systemTemp.createTemp('l10n-missing-key-');

    try {
      final libL10n = Directory('${temp.path}/lib/l10n')
        ..createSync(recursive: true);
      File('${libL10n.path}/app_en.arb').writeAsStringSync('''
{
  "appTitle": "Dental Commerce",
  "featureX": "Feature X",
  "@appTitle": { "description": "Title" },
  "@featureX": { "description": "Feature label" }
}
''');

      File('${libL10n.path}/app_ar.arb').writeAsStringSync('''
{
  "appTitle": "التجارة الطبية",
  "@appTitle": { "description": "Title ar" }
}
''');

      final script =
          File('${Directory.current.path}/../scripts/check-l10n-keys.sh')
                  .existsSync()
              ? '${Directory.current.path}/../scripts/check-l10n-keys.sh'
              : '${Directory.current.path}/../../scripts/check-l10n-keys.sh';

      final result = await Process.run(
        'bash',
        [script, '${libL10n.path}/app_en.arb', '${libL10n.path}/app_ar.arb'],
        runInShell: true,
      );

      expect(result.exitCode, isNonZero);
    } finally {
      await temp.delete(recursive: true);
    }
  });
}
