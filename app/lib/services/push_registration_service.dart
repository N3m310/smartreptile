import 'dart:async';

import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';

/// Keeps the signed-in account's FCM destination in sync with this app installation.
class PushRegistrationService {
  PushRegistrationService(
    this._api,
    this._requestPermission,
    this._getToken,
    this._tokenChanges,
  );

  final ApiClient _api;
  final Future<bool> Function() _requestPermission;
  final Future<String?> Function() _getToken;
  final Stream<String> _tokenChanges;

  StreamSubscription<String>? _tokenSubscription;
  bool _isSignedIn = false;

  /// Requests notification permission, then registers the current token on the account.
  Future<void> signedIn() async {
    _isSignedIn = true;
    _tokenSubscription ??= _tokenChanges.listen(
      (token) => unawaited(_registerToken(token)),
      onError: (Object error, StackTrace stackTrace) {
        debugPrint('FCM token refresh stream failed: $error\n$stackTrace');
      },
    );

    try {
      final permissionGranted = await _requestPermission();
      if (!_isSignedIn) return;

      if (!permissionGranted) {
        await _saveToken(null);
        return;
      }

      final token = await _getToken();
      if (!_isSignedIn) return;
      await _registerToken(token);
    } on FirebaseException catch (error, stackTrace) {
      debugPrint(
        'FCM setup failed: ${error.message ?? error.code}\n$stackTrace',
      );
    } on AppFailure catch (error) {
      debugPrint(
        'FCM registration failed: ${error.code} ${error.detail ?? ''}',
      );
    }
  }

  /// Clears this installation from the account before local sign-out completes.
  Future<void> signedOut() async {
    _isSignedIn = false;
    try {
      await _api.patchJson(
        '/api/v1/auth/me',
        body: const {'channelFcmEnabled': false, 'fcmToken': ''},
      );
    } on AppFailure catch (error) {
      debugPrint(
        'Could not clear the FCM registration: ${error.code} ${error.detail ?? ''}',
      );
    }
  }

  Future<void> _registerToken(String? token) async {
    if (!_isSignedIn) return;
    try {
      await _api.patchJson(
        '/api/v1/auth/me',
        body: {
          'channelFcmEnabled': token != null && token.isNotEmpty,
          'fcmToken': token ?? '',
        },
      );
    } on AppFailure catch (error) {
      debugPrint(
        'Could not update the FCM registration: ${error.code} ${error.detail ?? ''}',
      );
    }
  }

  Future<void> _saveToken(String? token) async {
    try {
      await _api.patchJson(
        '/api/v1/auth/me',
        body: {
          'channelFcmEnabled': token != null && token.isNotEmpty,
          'fcmToken': token ?? '',
        },
      );
    } on AppFailure catch (error) {
      debugPrint(
        'Could not update the FCM registration: ${error.code} ${error.detail ?? ''}',
      );
    }
  }
}
