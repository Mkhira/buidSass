import 'package:design_system/l10n/app_localizations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('AppLocalizations.appTitle is available and non-empty',
      (tester) async {
    late String title;

    await tester.pumpWidget(
      const MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: _LocalizedTitleProbe(),
      ),
    );

    final state = tester
        .state<_LocalizedTitleProbeState>(find.byType(_LocalizedTitleProbe));
    title = state.value;

    expect(title, isNotEmpty);
  });
}

class _LocalizedTitleProbe extends StatefulWidget {
  const _LocalizedTitleProbe();

  @override
  State<_LocalizedTitleProbe> createState() => _LocalizedTitleProbeState();
}

class _LocalizedTitleProbeState extends State<_LocalizedTitleProbe> {
  String value = '';

  @override
  Widget build(BuildContext context) {
    value = AppLocalizations.of(context).appTitle;
    return Text(value, textDirection: TextDirection.ltr);
  }
}
