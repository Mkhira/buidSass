import 'package:flutter/material.dart';

class AppTypography {
  const AppTypography._();

  static const TextStyle headline = TextStyle(
    fontSize: 24,
    height: 1.3,
    fontWeight: FontWeight.w700,
    letterSpacing: -0.2,
  );

  static const TextStyle body = TextStyle(
    fontSize: 14,
    height: 1.5,
    fontWeight: FontWeight.w400,
  );

  static const TextStyle caption = TextStyle(
    fontSize: 12,
    height: 1.4,
    fontWeight: FontWeight.w400,
    color: Color(0xFF6B7280),
  );
}
