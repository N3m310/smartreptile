import 'dart:async';

import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';
import '../data/token_store.dart';
import '../models/session_models.dart';
import '../services/push_registration_service.dart';

/// Who is signed in, and therefore what the app may show.
///
/// This is the class the screens ask for permission. `hasPermission` is the only gate the UI has — a role code is
/// carried for display and a `roleId` so an account screen can move an account onto another role, but neither may
/// decide whether a screen exists (`02-design/06` §3).
///
/// The refresh token is the only thing kept on disk, and it is kept in the platform keychain rather than in
/// preferences: it is a bearer credential for thirty days, and a file a backup can read is a file someone else can
/// read. The access token is never written — it is reissued from the refresh token on launch, so nothing short-lived
/// has to be stored at all.
class SessionProvider extends ChangeNotifier {
  /// Creates the provider and wires the client's renewal hook.
  SessionProvider({
    required this._api,
    TokenStore? store,
    PushRegistrationService? pushRegistration,
  }) : _store = store ?? TokenStore.secure() {
    _pushRegistration = pushRegistration;
    _api.onUnauthorized = _renew;
  }

  final ApiClient _api;
  final TokenStore _store;
  late final PushRegistrationService? _pushRegistration;

  AccountProfile? _user;
  String? _refreshToken;
  bool _isRestoring = false;
  bool _isBusy = false;
  bool _isRenewing = false;
  bool _isGuestMode = false;
  AppFailure? _lastFailure;

  /// The signed-in account, or null while nobody is.
  AccountProfile? get user => _user;

  /// True once an account is signed in.
  bool get isSignedIn => _user != null;

  /// True if keeper has bypassed login to explore in guest / demo mode.
  bool get isGuestMode => _isGuestMode;

  /// Sets guest / demo mode.
  void enterGuestMode() {
    _isGuestMode = true;
    notifyListeners();
  }

  /// Leaves guest mode to return to login.
  void leaveGuestMode() {
    _isGuestMode = false;
    notifyListeners();
  }

  /// True while a stored session is being restored; the shell waits rather than flashing the signed-out state.
  bool get isRestoring => _isRestoring;

  /// True while a sign-in or sign-out request is in flight.
  bool get isBusy => _isBusy;

  /// Why the last sign-in attempt failed, if it did.
  AppFailure? get lastFailure => _lastFailure;

  /// Clears the last failure state.
  void clearLastFailure() {
    _lastFailure = null;
    notifyListeners();
  }

  /// True when the account holds [code].
  bool hasPermission(String code) => _user?.hasPermission(code) ?? false;

  /// True when the account holds any of [codes].
  bool hasAnyPermission(Iterable<String> codes) =>
      _user?.hasAnyPermission(codes) ?? false;

  /// Restores the stored session, if there is one the server still accepts.
  ///
  /// A refresh token the server refuses is not shown as an error: it means the session ended while the app was
  /// closed, and the sign-in card is the whole answer to that. A network failure is different — a reachable-when-online
  /// session must not be discarded because the phone was in a tunnel.
  Future<void> restore() async {
    _isRestoring = true;
    notifyListeners();

    try {
      final stored = await _readRefreshToken();

      if (stored == null || stored.isEmpty) {
        return;
      }

      _refreshToken = stored;

      final payload = await _api.postJson(
        '/api/v1/auth/refresh',
        body: {'refreshToken': stored},
      );
      _apply(AuthSession.fromJson(payload));
      await _writeRefreshToken(_refreshToken);
      unawaited(_pushRegistration?.signedIn());
    } on AppFailure catch (failure) {
      if (failure.isNetworkFailure) {
        _lastFailure = failure;
      } else {
        await _forget();
      }
    } finally {
      _isRestoring = false;
      notifyListeners();
    }
  }

  /// Signs in and answers whether it worked.
  Future<bool> signIn({
    required String usernameOrEmail,
    required String password,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      final payload = await _api.postJson(
        '/api/v1/auth/login',
        body: {'usernameOrEmail': usernameOrEmail, 'password': password},
      );
      _apply(AuthSession.fromJson(payload));
      _isGuestMode = false;
      await _writeRefreshToken(_refreshToken);
      unawaited(_pushRegistration?.signedIn());
      return true;
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      return false;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Registers a new account, returning the newly issued single-use backup recovery code.
  Future<String> register({
    required String username,
    required String email,
    required String password,
    String? preferredLanguage,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      final payload = await _api.postJson(
        '/api/v1/auth/register',
        body: {
          'username': username,
          'email': email,
          'password': password,
          'preferredLanguage': ?preferredLanguage,
        },
      );
      return (payload['recoveryCode'] as String?) ?? '';
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      rethrow;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Recovers account using backup recovery code, rotating and returning the new backup code.
  Future<String> recover({
    required String usernameOrEmail,
    required String recoveryCode,
    required String newPassword,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      final payload = await _api.postJson(
        '/api/v1/auth/recover',
        body: {
          'usernameOrEmail': usernameOrEmail,
          'recoveryCode': recoveryCode,
          'newPassword': newPassword,
        },
      );
      return (payload['recoveryCode'] as String?) ?? '';
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      rethrow;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Requests a server-issued reset code (delivered to logs/email).
  Future<void> forgotPassword({
    required String usernameOrEmail,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      await _api.postJson(
        '/api/v1/auth/forgot-password',
        body: {'usernameOrEmail': usernameOrEmail},
      );
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      rethrow;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Resets password using a server-issued reset code, returning the new backup code.
  Future<String> resetPassword({
    required String usernameOrEmail,
    required String resetCode,
    required String newPassword,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      final payload = await _api.postJson(
        '/api/v1/auth/reset-password',
        body: {
          'usernameOrEmail': usernameOrEmail,
          'resetCode': resetCode,
          'newPassword': newPassword,
        },
      );
      return (payload['recoveryCode'] as String?) ?? '';
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      rethrow;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Changes the signed-in account's password.
  ///
  /// The refresh token travels with the request: it names the session that survives, every other one ends.
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    _isBusy = true;
    _lastFailure = null;
    notifyListeners();

    try {
      await _api.postJson(
        '/api/v1/auth/change-password',
        body: {
          'currentPassword': currentPassword,
          'newPassword': newPassword,
          'refreshToken': _refreshToken ?? '',
        },
      );
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      rethrow;
    } finally {
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Ends the session, locally and on the server.
  ///
  /// The server call is best-effort: a sign-out that failed because the phone is offline still has to sign this
  /// device out, or the button would be a lie the moment the network drops.
  Future<void> signOut() async {
    _isBusy = true;
    notifyListeners();

    final token = _refreshToken;

    try {
      await _pushRegistration?.signedOut();
      if (token != null && token.isNotEmpty) {
        await _api.postJson(
          '/api/v1/auth/logout',
          body: {'refreshToken': token},
        );
      }
    } on AppFailure {
      // Ignored on purpose: the local session is cleared below either way.
    } finally {
      await _forget();
      _isGuestMode = false;
      _isBusy = false;
      notifyListeners();
    }
  }

  /// Re-reads the profile, so a role change to one's own account takes effect without signing out and in.
  Future<void> refreshProfile() async {
    if (!isSignedIn) {
      return;
    }

    try {
      final payload = await _api.getJson('/api/v1/auth/me');
      _user = AccountProfile.fromJson(payload);
      notifyListeners();
    } on AppFailure catch (failure) {
      _lastFailure = failure;
      notifyListeners();
    }
  }

  /// Renews the access token. Answers false when there is nothing to renew with.
  ///
  /// Called by [ApiClient] when a request is refused with 401. The re-entrancy guard matters: the renewal itself
  /// travels through the same client, and a refused renewal must not start another renewal.
  Future<bool> _renew() async {
    if (_isRenewing) {
      return false;
    }

    final token = _refreshToken ?? await _readRefreshToken();

    if (token == null || token.isEmpty) {
      return false;
    }

    _isRenewing = true;

    try {
      final payload = await _api.postJson(
        '/api/v1/auth/refresh',
        body: {'refreshToken': token},
      );
      _apply(AuthSession.fromJson(payload));
      await _writeRefreshToken(_refreshToken);
      notifyListeners();
      return true;
    } on AppFailure {
      await _forget();
      notifyListeners();
      return false;
    } finally {
      _isRenewing = false;
    }
  }

  void _apply(AuthSession session) {
    _user = session.user;
    _refreshToken = session.refreshToken;
    _api.accessToken = session.accessToken;
    _lastFailure = null;
  }

  Future<void> _forget() async {
    _user = null;
    _refreshToken = null;
    _api.accessToken = null;
    await _deleteRefreshToken();
  }

  // Storage is best-effort in both directions, and the store itself owns that rule: a session that cannot be
  // persisted still works until the app closes, which is better than refusing to sign in.
  Future<String?> _readRefreshToken() => _store.read();

  Future<void> _writeRefreshToken(String? token) => _store.write(token);

  Future<void> _deleteRefreshToken() => _store.write(null);
}
