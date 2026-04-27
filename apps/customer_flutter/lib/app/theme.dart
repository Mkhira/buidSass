import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

/// AppTheme façade — the customer app reads every design token through this
/// indirection so feature code never imports the design-system package
/// directly. New tokens land in `packages/design_system/lib/tokens/` first.
class CustomerAppTheme {
  const CustomerAppTheme._();

  static ThemeData light() => AppTheme.light();
  static ThemeData dark() => AppTheme.dark();
}
