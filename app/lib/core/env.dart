/// Build-time environment (§03-implementation/01 §4).
///
/// The API base URL is injected with `--dart-define=API_BASE=…` so the same source builds for the emulator,
/// a LAN demo host and a tunnel. No secrets are ever compiled into the app (NFR-04).
library;

class Env {
  const Env._();

  /// API base URL, e.g. `http://10.0.2.2:8080` for the Android emulator.
  static const String apiBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'http://localhost:8080',
  );

  /// Version string shown in the about/diagnostics screen; must match the release build (rubric evidence).
  static const String appVersion = String.fromEnvironment(
    'APP_VERSION',
    defaultValue: '0.1.0',
  );

  /// Default sampling interval assumed when the device has not reported one yet.
  static const int defaultSamplingIntervalSec = 60;

  /// Whether to run the clickable prototype instead of the real (currently empty) data path.
  ///
  /// Default `true`, because until the M2 endpoints exist the live path can only show empty states. Override with
  /// `--dart-define=PROTOTYPE=false` to see the M1 screens against the real `ApiClient`.
  ///
  /// Both paths stay compiled on purpose: a prototype that is the only thing that still builds is a prototype nobody
  /// notices has drifted away from the data layer it is pretending to be.
  static const bool prototypeMode = bool.fromEnvironment(
    'PROTOTYPE',
    defaultValue: true,
  );

  /// Number of missed intervals after which a value is considered stale (FR-07 / BR-07.2).
  static const int staleAfterIntervals = 3;
}
