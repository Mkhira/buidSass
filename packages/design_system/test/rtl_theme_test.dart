import 'package:design_system/design_system.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('EdgeInsetsDirectional mirrors between LTR and RTL',
      (tester) async {
    const directional = EdgeInsetsDirectional.all(AppSpacing.md);

    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: Padding(
          padding: directional,
          child: const SizedBox(width: 10, height: 10),
        ),
      ),
    );

    final ltrPadding = tester
        .widget<Padding>(find.byType(Padding))
        .padding
        .resolve(TextDirection.ltr);

    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.rtl,
        child: Padding(
          padding: directional,
          child: const SizedBox(width: 10, height: 10),
        ),
      ),
    );

    final rtlPadding = tester
        .widget<Padding>(find.byType(Padding))
        .padding
        .resolve(TextDirection.rtl);

    expect(ltrPadding.left, equals(rtlPadding.right));
    expect(ltrPadding.right, equals(rtlPadding.left));
    expect(ltrPadding.top, equals(rtlPadding.top));
    expect(ltrPadding.bottom, equals(rtlPadding.bottom));
  });
}
