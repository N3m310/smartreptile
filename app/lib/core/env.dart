/// Build-time environment (§03-implementation/01 §4).
///
/// The API base URL is injected with `--dart-define=API_BASE=…` so the same source builds for the emulator,
/// a LAN demo host and a tunnel. No secrets are ever compiled into the app (NFR-04).
library;

import 'package:flutter/foundation.dart';

class Env {
  const Env._();

  /// API base URL, e.g. `http://10.0.2.2:8080` for the Android emulator.
  static String get apiBase {
    const fromEnv = String.fromEnvironment('API_BASE');
    if (fromEnv.isNotEmpty) return fromEnv;
    if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) {
      return 'http://10.0.2.2:8080';
    }
    return 'http://localhost:8080';
  }

  /// Firebase Android app configuration. These values are public client identifiers, not server credentials.
  static const firebaseApiKey = String.fromEnvironment('FIREBASE_API_KEY');
  static const firebaseAppId = String.fromEnvironment('FIREBASE_APP_ID');
  static const firebaseProjectId = String.fromEnvironment(
    'FIREBASE_PROJECT_ID',
  );
  static const firebaseMessagingSenderId = String.fromEnvironment(
    'FIREBASE_MESSAGING_SENDER_ID',
  );

  /// Whether this build has enough Firebase configuration to enable push registration.
  static bool get firebaseConfigured =>
      firebaseApiKey.isNotEmpty &&
      firebaseAppId.isNotEmpty &&
      firebaseProjectId.isNotEmpty &&
      firebaseMessagingSenderId.isNotEmpty;

  /// Version string shown in the about/diagnostics screen; must match the release build (rubric evidence).
  static const String appVersion = String.fromEnvironment(
    'APP_VERSION',
    defaultValue: '0.1.0',
  );

  /// Default sampling interval assumed when the device has not reported one yet.
  static const int defaultSamplingIntervalSec = 60;

  /// Whether to run the clickable prototype instead of the app's own screens.
  ///
  /// Default `false`: the app opens on the VIVARIUMGUARD shell (`app/lib/app.dart`), which is the client a keeper
  /// sees — the prototype is a working aid, not the product, and it still carries the pre-rename `SmartReptile`
  /// wordmark. Turn it on with `--dart-define=PROTOTYPE=true` for the Rule Lab and the scenario walkthroughs
  /// (`app/PROTOTYPE.md`).
  ///
  /// Both paths stay compiled on purpose: a prototype that is the only thing that still builds is a prototype nobody
  /// notices has drifted away from the data layer it is pretending to be.
  static const bool prototypeMode = bool.fromEnvironment(
    'PROTOTYPE',
    defaultValue: false,
  );

  /// Number of missed intervals after which a value is considered stale (FR-07 / BR-07.2).
  static const int staleAfterIntervals = 3;
}
