import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Theme and language preferences, persisted locally so the app opens the way the keeper left it.
class SettingsProvider extends ChangeNotifier {
  SettingsProvider({this._preferences});

  static const _themeKey = 'settings.themeMode';
  static const _localeKey = 'settings.locale';

  SharedPreferences? _preferences;

  ThemeMode _themeMode = ThemeMode.system;
  Locale? _locale;

  /// Current theme mode.
  ThemeMode get themeMode => _themeMode;

  /// Explicit locale, or null to follow the device locale (Vietnamese is the default template language).
  Locale? get locale => _locale;

  /// Loads persisted preferences. Safe to call when storage is unavailable (e.g. in a widget test).
  Future<void> load() async {
    try {
      _preferences ??= await SharedPreferences.getInstance();
    } on Exception {
      return; // no storage in this environment: keep the defaults rather than crashing on launch
    }

    final storedTheme = _preferences?.getString(_themeKey);
    _themeMode = switch (storedTheme) {
      'light' => ThemeMode.light,
      'dark' => ThemeMode.dark,
      _ => ThemeMode.system,
    };

    final storedLocale = _preferences?.getString(_localeKey);
    if (storedLocale != null && storedLocale.isNotEmpty) {
      _locale = Locale(storedLocale);
    }

    notifyListeners();
  }

  /// Sets and persists the theme mode.
  Future<void> setThemeMode(ThemeMode mode) async {
    _themeMode = mode;
    notifyListeners();
    await _preferences?.setString(_themeKey, switch (mode) {
      ThemeMode.light => 'light',
      ThemeMode.dark => 'dark',
      ThemeMode.system => 'system',
    });
  }

  /// Sets and persists the UI language.
  Future<void> setLocale(Locale? locale) async {
    _locale = locale;
    notifyListeners();
    await _preferences?.setString(_localeKey, locale?.languageCode ?? '');
  }
}
