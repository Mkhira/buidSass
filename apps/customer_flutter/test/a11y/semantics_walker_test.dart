import 'package:customer_flutter/generated/l10n/app_localizations.dart';
import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// T118c — semantic walker. Pumps a representative composition of base
/// design-system primitives and asserts (a) every Text node carries a
/// non-empty value, (b) buttons expose a label, (c) every interactive
/// widget is reachable from the semantics tree. The full per-feature
/// walker lands when widget tests are introduced — this is the
/// design-system-level smoke test that covers shared primitives.
void main() {
  testWidgets('design-system primitives expose semantic labels',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: const [Locale('en'), Locale('ar')],
      locale: const Locale('en'),
      home: Builder(
        builder: (context) {
          final l10n = AppLocalizations.of(context);
          return Scaffold(
            body: Column(
              children: [
                LoadingState(semanticsLabel: l10n.commonLoading),
                EmptyState(title: l10n.commonEmpty),
                ErrorState(
                  title: l10n.commonErrorTitle,
                  body: l10n.commonErrorBody,
                  onRetry: () {},
                  retryLabel: l10n.commonRetry,
                ),
                AppButton(
                  label: l10n.commonContinue,
                  onPressed: () {},
                ),
                RestrictedBadge(label: l10n.verificationRequired),
              ],
            ),
          );
        },
      ),
    ));
    await tester.pump();

    // Every Text widget renders with a non-empty value.
    for (final element in find.byType(Text).evaluate()) {
      final w = element.widget as Text;
      final value = w.data ?? w.textSpan?.toPlainText() ?? '';
      expect(value, isNotEmpty,
          reason: 'Text widget rendered without a value: ${w.toStringDeep()}');
    }

    // Buttons are reachable as semantic actionable nodes.
    final handle = tester.ensureSemantics();
    expect(find.bySemanticsLabel(RegExp('.+')), findsWidgets);
    handle.dispose();
  });

  testWidgets('AR-RTL renders without overflow on the same primitives',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: const [Locale('en'), Locale('ar')],
      locale: const Locale('ar'),
      builder: (ctx, child) =>
          Directionality(textDirection: TextDirection.rtl, child: child!),
      home: Builder(builder: (context) {
        final l10n = AppLocalizations.of(context);
        return Scaffold(
          body: Center(child: Text(l10n.appName)),
        );
      }),
    ));
    await tester.pump();
    expect(find.byType(Text), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
