/// The permission catalogue as the app spells it (`SmartReptile.Domain.Identity.Permissions`).
///
/// Screens never test a role *name*: they ask the session whether it holds a code. A deployment may re-point a role
/// at another permission set — that is the whole point of `RolePermission` being data — and a screen that tested for
/// `role == 'Admin'` would keep rendering the old answer after such an edit (§02-design/06 §3).
///
/// The codes are duplicated here rather than fetched, because a screen has to name the permission it requires
/// *before* it can call anything. The catalogue a role matrix edits still comes from
/// `GET /api/v1/admin/permissions`: this class only says which codes this build's screens gate on.
library;

class Permissions {
  const Permissions._();

  // The caller's own enclosure and boards.
  static const terrariumRead = 'terrarium.read';
  static const terrariumWrite = 'terrarium.write';
  static const deviceRead = 'device.read';
  static const deviceWrite = 'device.write';
  static const deviceProvision = 'device.provision';

  // Alerts.
  static const alertRead = 'alert.read';
  static const alertWrite = 'alert.write';
  static const silenceWrite = 'silence.write';

  // The keeper's own account.
  static const notificationRead = 'notification.read';
  static const auditRead = 'audit.read';

  // Shared catalogues.
  static const speciesRead = 'species.read';
  static const speciesWrite = 'species.write';

  // Accounts.
  static const userRead = 'user.read';
  static const userManage = 'user.manage';

  // The two that reach across accounts.
  static const dataReadAny = 'data.read.any';
  static const dataWriteAny = 'data.write.any';

  /// Every code this build knows, in catalogue order.
  static const List<String> all = [
    terrariumRead,
    terrariumWrite,
    deviceRead,
    deviceWrite,
    deviceProvision,
    alertRead,
    alertWrite,
    silenceWrite,
    notificationRead,
    auditRead,
    speciesRead,
    speciesWrite,
    userRead,
    userManage,
    dataReadAny,
    dataWriteAny,
  ];

  /// The two codes that reach data outside the caller's own account.
  ///
  /// Used only as a fallback for a session whose permission rows were not loaded; the catalogue's own
  /// `crossesAccounts` flag is what the matrix renders.
  static const List<String> crossAccountCodes = [dataReadAny, dataWriteAny];
}

/// The four roles the product ships, by code (`SystemRoles`).
///
/// Named so a list can label a row ("this is the built-in Owner") and so a create form can offer the shipped four
/// when the role list could not be read. Never used to decide what a screen shows.
class SystemRoleCodes {
  const SystemRoleCodes._();

  /// The system administrator.
  static const admin = 'Admin';

  /// The keeper who owns enclosures.
  static const owner = 'Owner';

  /// A helper who may handle alerts but not reshape an enclosure.
  static const technician = 'Technician';

  /// A read-only account.
  static const viewer = 'Viewer';

  /// The four, in display order.
  static const List<String> all = [admin, owner, technician, viewer];
}
