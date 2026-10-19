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
    this.errors = const [],
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

  /// Field-level violations from the problem body, if any.
  final List<dynamic> errors;

  /// True when the request could not reach the server at all.
  bool get isNetworkFailure => statusCode == 0;

  /// Localisation key for the most useful message we can give for this failure.
  ///
  /// The administrative codes are mapped by name rather than by status, because the account and role rules refuse
  /// with a *reason* — "that is the last administrator", "a role with accounts cannot be deleted" — and a screen
  /// that only said "the server refused the request" would be throwing away the one sentence the caller needs
  /// (FR-18). Anything unlisted still falls back to the status, so no code can render a blank error.
  String get messageKey {
    if (isNetworkFailure) {
      return 'errorBackendUnreachable';
    }

    switch (code) {
      case 'invalid_credentials':
        return 'invalidCredentials';
      case 'account_locked':
        return 'accountLocked';
      case 'rate_limited':
        return 'rateLimited';
      case 'token_invalid':
      case 'token_reused':
      case 'token_missing':
      case 'unauthenticated':
        return 'unauthenticated';
      case 'registration_invalid':
        return 'registrationInvalid';
      case 'registration_conflict':
        return 'usernameTaken';
      case 'recovery_failed':
        return 'recoveryFailed';
      case 'identifier_unknown':
        return 'identifierUnknown';
      case 'user_not_found':
        return 'adminAccountNotFound';
      case 'self_change_refused':
        return 'adminErrorSelfChange';
      case 'last_administrator_refused':
        return 'adminErrorLastAdministrator';
      case 'account_in_use':
        return 'adminErrorAccountInUse';
      case 'account_invalid':
        return 'adminErrorAccountInvalid';
      case 'password_invalid':
      case 'password_policy_violation':
        return 'passwordPolicyViolation';
      case 'role_invalid':
        return 'adminErrorRoleInvalid';
      case 'role_not_found':
        return 'adminErrorRoleNotFound';
      case 'role_in_use':
        return 'adminErrorRoleInUse';
      case 'system_role_immutable':
        return 'adminErrorSystemRole';
      case 'permission_unknown':
        return 'adminErrorPermissionUnknown';
      default:
        return switch (statusCode) {
          401 => 'unauthenticated',
          403 => 'errorForbidden',
          404 => 'errorNotFound',
          409 => 'adminErrorConflict',
          _ => 'errorGeneric',
        };
    }
  }

  @override
  String toString() => 'AppFailure($statusCode $code)';
}
