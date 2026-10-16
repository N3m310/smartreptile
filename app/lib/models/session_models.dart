/// The signed-in account, as `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` and `GET /api/v1/auth/me`
/// describe it.
///
/// `permissions` — not `role` — is what the app gates its screens on. The access token carries the same codes, so
/// the two agree by construction; `role` is here to be *shown*, and `roleId` so an account screen can move the
/// account onto another role.
class AccountProfile {
  /// Creates a profile; [permissions] is snapshotted into a set so `hasPermission` is a lookup rather than a scan.
  AccountProfile({
    required this.id,
    required this.username,
    required this.email,
    required this.role,
    required this.roleId,
    required List<String> permissions,
    required this.preferredLanguage,
    required this.timeZoneId,
    this.quietHoursStart,
    this.quietHoursEnd,
    this.minNotifySeverity = 'warning',
  }) : permissions = List.unmodifiable(permissions),
       _grants = permissions.toSet();

  /// Account id.
  final String id;

  /// Login name.
  final String username;

  /// Email address.
  final String email;

  /// Code of the role the account holds, e.g. `Owner`. Display only.
  final String role;

  /// The role's identity, for a "change role" call.
  final String roleId;

  /// Permission codes the account's role grants, in catalogue order.
  final List<String> permissions;

  final String preferredLanguage;
  final String timeZoneId;
  final String? quietHoursStart;
  final String? quietHoursEnd;
  final String minNotifySeverity;

  final Set<String> _grants;

  /// True when the account holds [code].
  ///
  /// Compared exactly, as the server compares it (`StringComparer.Ordinal`): a code the API did not grant is not a
  /// permission, and folding case here would hide a typo the API would refuse.
  bool hasPermission(String code) => _grants.contains(code);

  /// True when any of [codes] is held — for the "may open this tab at all" questions that span two codes.
  bool hasAnyPermission(Iterable<String> codes) => codes.any(_grants.contains);

  /// Builds a profile from a login, refresh or `/auth/me` payload.
  factory AccountProfile.fromJson(Map<String, dynamic> json) => AccountProfile(
    id: json['id'] as String? ?? '',
    username: json['username'] as String? ?? '',
    email: json['email'] as String? ?? '',
    role: json['role'] as String? ?? '',
    roleId: json['roleId'] as String? ?? '',
    permissions: [
      for (final code in (json['permissions'] as List?) ?? const [])
        if (code is String) code,
    ],
    preferredLanguage: json['preferredLanguage'] as String? ?? 'vi',
    timeZoneId: json['timeZoneId'] as String? ?? '',
    quietHoursStart: json['quietHoursStart'] as String?,
    quietHoursEnd: json['quietHoursEnd'] as String?,
    minNotifySeverity: json['minNotifySeverity'] as String? ?? 'warning',
  );
}

/// A session: the tokens a client stores plus the profile it may render immediately.
class AuthSession {
  /// Creates a session.
  const AuthSession({
    required this.user,
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresAtUtc,
    required this.refreshTokenExpiresAtUtc,
  });

  /// Owner of the session.
  final AccountProfile user;

  /// Short-lived JWT, sent as `Authorization: Bearer`.
  final String accessToken;

  /// Opaque token, returned exactly once per rotation.
  final String refreshToken;

  /// When the JWT stops being accepted.
  final DateTime accessTokenExpiresAtUtc;

  /// When the refresh token stops being accepted.
  final DateTime refreshTokenExpiresAtUtc;

  /// Builds a session from a login or refresh payload.
  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
    user: AccountProfile.fromJson(
      (json['user'] as Map<String, dynamic>?) ?? const {},
    ),
    accessToken: json['accessToken'] as String? ?? '',
    refreshToken: json['refreshToken'] as String? ?? '',
    accessTokenExpiresAtUtc:
        DateTime.tryParse(
          json['accessTokenExpiresAtUtc'] as String? ?? '',
        )?.toUtc() ??
        DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
    refreshTokenExpiresAtUtc:
        DateTime.tryParse(
          json['refreshTokenExpiresAtUtc'] as String? ?? '',
        )?.toUtc() ??
        DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
  );
}
