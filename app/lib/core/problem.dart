/// A failure the UI can explain: an API problem code plus the HTTP status.
///
/// Every unexpected code still maps to a message derived from the status, so the app can never render
/// "Error 500" with no explanation (`03-implementation/05` §4).
library;

class AppFailure implements Exception {
  const AppFailure({
    required this.code,
    required this.statusCode,
    this.title,
    this.detail,
    this.traceId,
  });

  /// Stable machine-readable code from the RFC 7807 body, e.g. `claim_code_invalid`.
  final String code;

  /// HTTP status, or 0 when the request never reached the server.
  final int statusCode;

  /// Short human-readable title from the problem body.
  final String? title;

  /// Longer explanation from the problem body.
  final String? detail;

  /// Correlation id echoed by the API — quoted when reporting a bug.
  final String? traceId;

  /// True when the request could not reach the server at all.
  bool get isNetworkFailure => statusCode == 0;

  /// Localisation key for the most useful message we can give for this failure.
  String get messageKey {
    if (isNetworkFailure) {
      return 'errorBackendUnreachable';
    }

    switch (code) {
      case 'invalid_credentials':
      case 'token_invalid':
      case 'token_reused':
        return 'errorGeneric';
      case 'rate_limited':
        return 'errorGeneric';
      default:
        return 'errorGeneric';
    }
  }

  @override
  String toString() => 'AppFailure($statusCode $code)';
}
