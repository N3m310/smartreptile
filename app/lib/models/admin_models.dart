/// Models for the administrator's surfaces (`/api/v1/admin/*` and `/api/v1/users`).
///
/// One file per surface would be truer to the contracts, but these types are read *together*: the overview names
/// the accounts, the lists name the owners, and the accounts screen needs a role. Keeping them apart would mean
/// three files that all import each other.
///
/// Every parser degrades rather than throws: a member the server did not send becomes an empty string or a zero, so
/// a field the API renames shows up as a blank cell instead of a red screen (`03-implementation/05` §4).
library;

import 'session_models.dart';

int _int(Object? value) => (value as num?)?.toInt() ?? 0;

int? _intOrNull(Object? value) => (value as num?)?.toInt();

String _str(Object? value) => value as String? ?? '';

String? _strOrNull(Object? value) => value as String?;

double? _doubleOrNull(Object? value) => (value as num?)?.toDouble();

DateTime? _date(Object? value) =>
    value is String ? DateTime.tryParse(value)?.toUtc() : null;

List<String> _strings(Object? value) => [
  for (final entry in (value as List?) ?? const [])
    if (entry is String) entry,
];

Map<String, dynamic> _map(Object? value) =>
    value is Map<String, dynamic> ? value : const {};

/// One page of an administrative list, with the total so a screen can render a pager.
class ApiPage<T> {
  /// Creates a page.
  const ApiPage({
    required this.items,
    required this.total,
    required this.skip,
    required this.take,
  });

  /// Rows on this page.
  final List<T> items;

  /// How many rows match the same filter.
  final int total;

  /// Rows skipped to reach this page.
  final int skip;

  /// Page size this answer was asked for.
  final int take;

  /// Parses `{ items, total, skip, take }`, using [parse] for each row.
  factory ApiPage.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic> row) parse,
  ) => ApiPage(
    items: [
      for (final row in (json['items'] as List?) ?? const [])
        if (row is Map<String, dynamic>) parse(row),
    ],
    total: _int(json['total']),
    skip: _int(json['skip']),
    take: _int(json['take']),
  );

  /// An empty first page, for a list that has not been read yet.
  static ApiPage<T> empty<T>() =>
      ApiPage<T>(items: const [], total: 0, skip: 0, take: 0);
}

/// The deployment-wide figures the dashboard's header shows.
class AdminCounts {
  /// Creates the counts.
  const AdminCounts({
    required this.accounts,
    required this.accountsActive,
    required this.terrariums,
    required this.terrariumsWithDevice,
    required this.devices,
    required this.devicesOnline,
    required this.alertsOpen,
    required this.alertsOpenEnclosure,
    required this.alertsOpenSpecies,
    required this.alertsCritical,
    required this.speciesProfiles,
  });

  final int accounts;
  final int accountsActive;
  final int terrariums;
  final int terrariumsWithDevice;
  final int devices;
  final int devicesOnline;
  final int alertsOpen;
  final int alertsOpenEnclosure;
  final int alertsOpenSpecies;
  final int alertsCritical;
  final int speciesProfiles;

  /// Parses `counts`.
  factory AdminCounts.fromJson(Map<String, dynamic> json) => AdminCounts(
    accounts: _int(json['accounts']),
    accountsActive: _int(json['accountsActive']),
    terrariums: _int(json['terrariums']),
    terrariumsWithDevice: _int(json['terrariumsWithDevice']),
    devices: _int(json['devices']),
    devicesOnline: _int(json['devicesOnline']),
    alertsOpen: _int(json['alertsOpen']),
    alertsOpenEnclosure: _int(json['alertsOpenEnclosure']),
    alertsOpenSpecies: _int(json['alertsOpenSpecies']),
    alertsCritical: _int(json['alertsCritical']),
    speciesProfiles: _int(json['speciesProfiles']),
  );
}

/// One account's holdings, as the overview's get-all table lists them.
class AdminAccountRollup {
  /// Creates a rollup row.
  const AdminAccountRollup({
    required this.userId,
    required this.username,
    required this.email,
    required this.role,
    required this.isActive,
    required this.terrariums,
    required this.devices,
    required this.alertsOpen,
    this.createdAt,
    this.lastLoginAt,
  });

  final String userId;
  final String username;
  final String email;
  final String role;
  final bool isActive;
  final int terrariums;
  final int devices;
  final int alertsOpen;
  final DateTime? createdAt;
  final DateTime? lastLoginAt;

  /// Parses one `accounts[]` entry.
  factory AdminAccountRollup.fromJson(Map<String, dynamic> json) =>
      AdminAccountRollup(
        userId: _str(json['userId']),
        username: _str(json['username']),
        email: _str(json['email']),
        role: _str(json['role']),
        isActive: json['isActive'] as bool? ?? false,
        terrariums: _int(json['terrariums']),
        devices: _int(json['devices']),
        alertsOpen: _int(json['alertsOpen']),
        createdAt: _date(json['createdAt']),
        lastLoginAt: _date(json['lastLoginAt']),
      );
}

/// One alert family, described the way the dashboard's family cards need it.
class AdminFamily {
  /// Creates a family description.
  const AdminFamily({
    required this.key,
    required this.metrics,
    required this.alertsOpen,
  });

  /// Key a client filters on: `enclosure` or `species`.
  final String key;

  /// Metric keys belonging to it.
  final List<String> metrics;

  /// Open alerts of this family across the deployment.
  final int alertsOpen;

  /// Parses one `families[]` entry.
  factory AdminFamily.fromJson(Map<String, dynamic> json) => AdminFamily(
    key: _str(json['key']),
    metrics: _strings(json['metrics']),
    alertsOpen: _int(json['alertsOpen']),
  );
}

/// What `GET /api/v1/admin/overview` answers.
class AdminOverview {
  /// Creates an overview.
  const AdminOverview({
    required this.counts,
    required this.accounts,
    required this.families,
  });

  final AdminCounts counts;
  final List<AdminAccountRollup> accounts;
  final List<AdminFamily> families;

  /// Parses the payload.
  factory AdminOverview.fromJson(Map<String, dynamic> json) => AdminOverview(
    counts: AdminCounts.fromJson(_map(json['counts'])),
    accounts: [
      for (final row in (json['accounts'] as List?) ?? const [])
        if (row is Map<String, dynamic>) AdminAccountRollup.fromJson(row),
    ],
    families: [
      for (final row in (json['families'] as List?) ?? const [])
        if (row is Map<String, dynamic>) AdminFamily.fromJson(row),
    ],
  );
}

/// One enclosure of any account, with its owner and what is wrong with it.
class AdminTerrarium {
  /// Creates a row.
  const AdminTerrarium({
    required this.id,
    required this.name,
    required this.ownerId,
    required this.ownerUsername,
    required this.speciesProfileId,
    required this.speciesProfileName,
    required this.status,
    required this.openAlerts,
    this.devicePublicId,
    this.deviceStatus,
    this.deviceLastSeenAt,
    this.createdAt,
  });

  final String id;
  final String name;
  final String ownerId;
  final String ownerUsername;
  final String speciesProfileId;
  final String speciesProfileName;
  final String status;
  final int openAlerts;
  final String? devicePublicId;
  final String? deviceStatus;
  final DateTime? deviceLastSeenAt;
  final DateTime? createdAt;

  /// Parses one row.
  factory AdminTerrarium.fromJson(Map<String, dynamic> json) => AdminTerrarium(
    id: _str(json['id']),
    name: _str(json['name']),
    ownerId: _str(json['ownerId']),
    ownerUsername: _str(json['ownerUsername']),
    speciesProfileId: _str(json['speciesProfileId']),
    speciesProfileName: _str(json['speciesProfileName']),
    status: _str(json['status']),
    openAlerts: _int(json['openAlerts']),
    devicePublicId: _strOrNull(json['devicePublicId']),
    deviceStatus: _strOrNull(json['deviceStatus']),
    deviceLastSeenAt: _date(json['deviceLastSeenAt']),
    createdAt: _date(json['createdAt']),
  );
}

/// One board in the deployment, bound or not.
class AdminDevice {
  /// Creates a row.
  const AdminDevice({
    required this.id,
    required this.publicId,
    required this.deviceName,
    required this.status,
    required this.firmwareVersion,
    this.ownerUsername,
    this.terrariumId,
    this.terrariumName,
    this.lastSeenAt,
    this.signalStrengthDbm,
    this.batteryPct,
  });

  final String id;
  final String publicId;
  final String deviceName;
  final String status;
  final String firmwareVersion;
  final String? ownerUsername;
  final String? terrariumId;
  final String? terrariumName;
  final DateTime? lastSeenAt;
  final int? signalStrengthDbm;
  final double? batteryPct;

  /// Parses one row.
  factory AdminDevice.fromJson(Map<String, dynamic> json) => AdminDevice(
    id: _str(json['id']),
    publicId: _str(json['publicId']),
    deviceName: _str(json['deviceName']),
    status: _str(json['status']),
    firmwareVersion: _str(json['firmwareVersion']),
    ownerUsername: _strOrNull(json['ownerUsername']),
    terrariumId: _strOrNull(json['terrariumId']),
    terrariumName: _strOrNull(json['terrariumName']),
    lastSeenAt: _date(json['lastSeenAt']),
    signalStrengthDbm: _intOrNull(json['signalStrengthDbm']),
    batteryPct: _doubleOrNull(json['batteryPct']),
  );
}

/// One alert of any account — which is what makes the two families reviewable across the deployment.
class AdminAlert {
  /// Creates a row.
  const AdminAlert({
    required this.id,
    required this.family,
    required this.severity,
    required this.state,
    required this.source,
    required this.deviceId,
    required this.terrariumId,
    required this.terrariumName,
    required this.ownerUsername,
    this.metric,
    this.triggeredAt,
    this.resolvedAt,
  });

  final int id;
  final String family;
  final String severity;
  final String state;
  final String source;
  final String deviceId;
  final String terrariumId;
  final String terrariumName;
  final String ownerUsername;
  final String? metric;
  final DateTime? triggeredAt;
  final DateTime? resolvedAt;

  /// True while the alert still needs someone (open or acknowledged).
  bool get isOpen => state != 'Resolved';

  /// Parses one row.
  factory AdminAlert.fromJson(Map<String, dynamic> json) => AdminAlert(
    id: _int(json['id']),
    family: _str(json['family']),
    severity: _str(json['severity']),
    state: _str(json['state']),
    source: _str(json['source']),
    deviceId: _str(json['deviceId']),
    terrariumId: _str(json['terrariumId']),
    terrariumName: _str(json['terrariumName']),
    ownerUsername: _str(json['ownerUsername']),
    metric: _strOrNull(json['metric']),
    triggeredAt: _date(json['triggeredAt']),
    resolvedAt: _date(json['resolvedAt']),
  );
}

/// One account, as `/api/v1/users` describes it.
class AccountSummary {
  /// Creates a row.
  AccountSummary({
    required this.id,
    required this.username,
    required this.email,
    required this.role,
    required this.roleId,
    required List<String> permissions,
    required this.preferredLanguage,
    required this.timeZoneId,
    required this.minNotifySeverity,
    this.quietHoursStart,
    this.quietHoursEnd,
    this.createdAt,
    this.lastLoginAt,
    this.disabledAt,
    required this.isActive,
  }) : permissions = List.unmodifiable(permissions),
       _grants = permissions.toSet();

  final String id;
  final String username;
  final String email;
  final String role;
  final String roleId;
  final List<String> permissions;
  final String preferredLanguage;
  final String timeZoneId;
  final String minNotifySeverity;
  final String? quietHoursStart;
  final String? quietHoursEnd;
  final DateTime? createdAt;
  final DateTime? lastLoginAt;
  final DateTime? disabledAt;
  final bool isActive;

  final Set<String> _grants;

  /// True when the account's role grants [code], from the codes the list carried on the row.
  bool hasPermission(String code) => _grants.contains(code);

  /// The profile this row describes, so a screen can render it with the session's own widget.
  AccountProfile toProfile() => AccountProfile(
    id: id,
    username: username,
    email: email,
    role: role,
    roleId: roleId,
    permissions: permissions,
    preferredLanguage: preferredLanguage,
    timeZoneId: timeZoneId,
    quietHoursStart: quietHoursStart,
    quietHoursEnd: quietHoursEnd,
    minNotifySeverity: minNotifySeverity,
  );

  /// Parses one row.
  factory AccountSummary.fromJson(Map<String, dynamic> json) => AccountSummary(
    id: _str(json['id']),
    username: _str(json['username']),
    email: _str(json['email']),
    role: _str(json['role']),
    roleId: _str(json['roleId']),
    permissions: _strings(json['permissions']),
    preferredLanguage: _str(json['preferredLanguage']),
    timeZoneId: _str(json['timeZoneId']),
    minNotifySeverity: _str(json['minNotifySeverity']),
    quietHoursStart: _strOrNull(json['quietHoursStart']),
    quietHoursEnd: _strOrNull(json['quietHoursEnd']),
    createdAt: _date(json['createdAt']),
    lastLoginAt: _date(json['lastLoginAt']),
    disabledAt: _date(json['disabledAt']),
    isActive: json['isActive'] as bool? ?? false,
  );
}

/// One role, as the matrix renders it.
class RoleSummary {
  /// Creates a role.
  const RoleSummary({
    required this.id,
    required this.code,
    required this.name,
    required this.description,
    required this.isSystem,
    required this.rank,
    required this.permissions,
    required this.crossAccountPermissions,
    required this.userCount,
    this.rowVersion,
  });

  final String id;
  final String code;
  final String name;
  final String description;
  final bool isSystem;
  final int rank;
  final List<String> permissions;
  final List<String> crossAccountPermissions;
  final int userCount;

  /// Concurrency token the server returned; echoed on a save so a stale edit is refused rather than applied.
  final String? rowVersion;

  /// True when this role reaches past the caller's own account — the answer to "which data does this role map to".
  bool get crossesAccounts => crossAccountPermissions.isNotEmpty;

  /// Parses one role.
  factory RoleSummary.fromJson(Map<String, dynamic> json) => RoleSummary(
    id: _str(json['id']),
    code: _str(json['code']),
    name: _str(json['name']),
    description: _str(json['description']),
    isSystem: json['isSystem'] as bool? ?? false,
    rank: _int(json['rank']),
    permissions: _strings(json['permissions']),
    crossAccountPermissions: _strings(json['crossAccountPermissions']),
    userCount: _int(json['userCount']),
    rowVersion: _strOrNull(json['rowVersion']),
  );
}

/// One permission in the catalogue, as the matrix offers it for granting.
class PermissionSummary {
  /// Creates a catalogue entry.
  const PermissionSummary({
    required this.code,
    required this.group,
    required this.name,
    required this.description,
    required this.crossesAccounts,
  });

  final String code;
  final String group;
  final String name;
  final String description;

  /// True when granting it reaches data outside the caller's own account.
  final bool crossesAccounts;

  /// Parses one catalogue entry.
  factory PermissionSummary.fromJson(Map<String, dynamic> json) =>
      PermissionSummary(
        code: _str(json['code']),
        group: _str(json['group']),
        name: _str(json['name']),
        description: _str(json['description']),
        crossesAccounts: json['crossesAccounts'] as bool? ?? false,
      );
}
