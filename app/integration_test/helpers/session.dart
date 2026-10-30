import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Clears everything the app persists between launches, so each case starts on the sign-in screen.
///
/// Two stores, two reasons:
///  * `flutter_secure_storage` holds the refresh token (`app/lib/data/token_store.dart`). Without this, the second
///    case in a run restores the first case's session and every "first launch" assertion lies.
///  * `shared_preferences` holds the chosen language (`SettingsProvider.load`); a language switch in one case would
///    otherwise decide the copy another case asserts on.
///
/// `adb shell pm clear` (which `scripts/e2e.ps1` runs before each suite) does the same thing between suites; this is
/// the between-cases half.
Future<void> clearStoredState() async {
  await const FlutterSecureStorage().deleteAll();
  final prefs = await SharedPreferences.getInstance();
  await prefs.clear();
}

/// Reads the stored refresh token, so a case can prove sign-out actually cleared the keychain rather than merely
/// navigating away from the shell.
Future<String?> storedRefreshToken() =>
    const FlutterSecureStorage().read(key: 'session.refreshToken');
