import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/admin_models.dart';

/// Role administration: `/api/v1/admin/roles` and the permission catalogue (FR-18).
///
/// The catalogue is read once and kept: it is code, not data — it changes only when the software does — and the
/// matrix needs it on every render to group seventeen rows. The roles themselves are re-read after every write,
/// because a save returns the role and the list's counts (how many accounts hold it) come from the list.
class RolesProvider extends ChangeNotifier {
  /// Creates the provider.
  RolesProvider({required this._api});

  final ApiClient _api;

  List<RoleSummary>? _roles;
  List<PermissionSummary>? _catalogue;
  bool _isLoading = false;
  bool _isSaving = false;
  AppFailure? _failure;

  /// Every role, lowest rank first, or null while they have never been read.
  List<RoleSummary>? get roles => _roles;

  /// The permission catalogue, or null while it has never been read.
  List<PermissionSummary>? get catalogue => _catalogue;

  /// True while the list is being read.
  bool get isLoading => _isLoading;

  /// True while a write is in flight.
  bool get isSaving => _isSaving;

  /// Why the last read failed.
  AppFailure? get failure => _failure;

  /// Reads the roles and, if needed, the catalogue.
  Future<void> load({bool includeCatalogue = true}) async {
    _isLoading = true;
    _failure = null;
    notifyListeners();

    try {
      final roles = await _api.getJson('/api/v1/admin/roles');
      _roles = [
        for (final row in (roles['items'] as List?) ?? const [])
          if (row is Map<String, dynamic>) RoleSummary.fromJson(row),
      ];

      if (includeCatalogue && _catalogue == null) {
        final catalogue = await _api.getJson('/api/v1/admin/permissions');
        _catalogue = [
          for (final row in (catalogue['items'] as List?) ?? const [])
            if (row is Map<String, dynamic>) PermissionSummary.fromJson(row),
        ];
      }
    } on AppFailure catch (failure) {
      _failure = failure;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Saves a role's name, description, order and — the one the matrix edits — its whole permission set.
  ///
  /// `permissions` replaces the set rather than adding to it: revoking is leaving a code out, because a merge would
  /// make a permission impossible to take away.
  Future<AppFailure?> saveRole(
    RoleSummary role, {
    String? name,
    String? description,
    int? rank,
    required Set<String> permissions,
  }) => _mutate(
    () => _api.patchJson(
      '/api/v1/admin/roles/${role.id}',
      body: {
        'name': name,
        'description': description,
        'rank': rank,
        'permissions': permissions.toList()..sort(),
        // The token the list handed back. The API's `UpdateRoleRequest` does not read it yet — the row's
        // `rowversion` is a concurrency token at the database level, so a stale write is refused there — but sending
        // what we read back is what makes this call correct the day the endpoint starts checking it.
        'rowVersion': role.rowVersion,
      },
    ),
  );

  /// Creates a custom role.
  Future<AppFailure?> createRole({
    required String code,
    required String name,
    String? description,
    int? rank,
    required Set<String> permissions,
  }) => _mutate(
    () => _api.postJson(
      '/api/v1/admin/roles',
      body: {
        'code': code,
        'name': name,
        'description': description,
        'rank': rank,
        'permissions': permissions.toList()..sort(),
      },
    ),
  );

  /// Deletes a custom role. A system role, or one an account still holds, is refused by the server.
  Future<AppFailure?> deleteRole(RoleSummary role) =>
      _mutate(() => _api.deleteJson('/api/v1/admin/roles/${role.id}'));

  /// Drops everything, so a sign-out cannot leave the deployment's matrix on screen.
  void clear() {
    _roles = null;
    _catalogue = null;
    _failure = null;
    notifyListeners();
  }

  Future<AppFailure?> _mutate(Future<Map<String, dynamic>> Function() request) async {
    _isSaving = true;
    notifyListeners();

    try {
      await request();
      await load();
      return null;
    } on AppFailure catch (failure) {
      return failure;
    } finally {
      _isSaving = false;
      notifyListeners();
    }
  }
}
