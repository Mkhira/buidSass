import 'package:design_system/src/tokens/app_colors.dart';
import 'package:design_system/src/tokens/app_typography.dart';
import 'package:flutter/material.dart';

class AppTheme {
  const AppTheme._();

  static ThemeData light() {
    final scheme = ColorScheme.fromSeed(
      seedColor: AppColors.primary,
      primary: AppColors.primary,
      secondary: AppColors.secondary,
      surface: Colors.white,
    );

    return ThemeData(
      colorScheme: scheme,
      scaffoldBackgroundColor: Colors.white,
      appBarTheme: const AppBarTheme(
          backgroundColor: AppColors.primary, foregroundColor: Colors.white),
      textTheme: const TextTheme(
        headlineMedium: AppTypography.headline,
        bodyMedium: AppTypography.body,
        bodySmall: AppTypography.caption,
      ),
      useMaterial3: true,
    );
  }

  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(
      brightness: Brightness.dark,
      seedColor: AppColors.primary,
      primary: AppColors.primary,
      secondary: AppColors.accent,
    );

    return ThemeData(
      colorScheme: scheme,
      scaffoldBackgroundColor: const Color(0xFF121212),
      appBarTheme: const AppBarTheme(
          backgroundColor: AppColors.primary, foregroundColor: Colors.white),
      textTheme: const TextTheme(
        headlineMedium: AppTypography.headline,
        bodyMedium: AppTypography.body,
        bodySmall: AppTypography.caption,
      ),
      useMaterial3: true,
    );
  }
}
