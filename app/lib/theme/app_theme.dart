import 'package:flutter/material.dart';

/// TerraGuard Theme & Design Tokens
class AppColors {
  static const bgMain = Color(0xFF0B1A0D);
  static const bgCard = Color(0xFF112016);
  static const bgCardHover = Color(0xFF162A1D);
  static const bgInput = Color(0xFF0E1D11);
  static const border = Color(0xFF1E3825);
  static const textMain = Color(0xFFDCD5C4);
  static const textMuted = Color(0xFF8E9E8F);
  static const primary = Color(0xFF4A9E6A);
  static const primaryDark = Color(0xFF3D8558);
  static const accent = Color(0xFFC87F3A);

  // Status colors
  static const statusNormal = Color(0xFF4A9E6A);
  static const statusWarning = Color(0xFFE8A832);
  static const statusDanger = Color(0xFFE05530);
  static const statusOffline = Color(0xFF556055);
}

class AppTheme {
  static ThemeData get darkTheme {
    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: AppColors.bgMain,
      colorScheme: const ColorScheme.dark(
        primary: AppColors.primary,
        surface: AppColors.bgCard,
        surfaceContainerHighest: AppColors.bgCardHover,
        onSurface: AppColors.textMain,
        error: AppColors.statusDanger,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: AppColors.bgCard,
        foregroundColor: AppColors.textMain,
        elevation: 0,
        centerTitle: false,
        shape: Border(bottom: BorderSide(color: AppColors.border, width: 1)),
      ),
      cardTheme: const CardThemeData(
        color: AppColors.bgCard,
        elevation: 0,
        shape: RoundedRectangleBorder(
          side: BorderSide(color: AppColors.border, width: 1),
          borderRadius: BorderRadius.all(Radius.circular(20)),
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: AppColors.bgCard,
        indicatorColor: AppColors.primary.withValues(alpha: 0.25),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return const IconThemeData(color: AppColors.primary);
          }
          return const IconThemeData(color: AppColors.textMuted);
        }),
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return const TextStyle(
              color: AppColors.primary,
              fontWeight: FontWeight.bold,
              fontSize: 11,
            );
          }
          return const TextStyle(color: AppColors.textMuted, fontSize: 11);
        }),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppColors.bgInput,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 12,
        ),
        enabledBorder: OutlineInputBorder(
          borderSide: const BorderSide(color: AppColors.border),
          borderRadius: BorderRadius.circular(12),
        ),
        focusedBorder: OutlineInputBorder(
          borderSide: const BorderSide(color: AppColors.primary, width: 1.5),
          borderRadius: BorderRadius.circular(12),
        ),
        hintStyle: const TextStyle(color: AppColors.textMuted, fontSize: 13),
      ),
    );
  }
}
