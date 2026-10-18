import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/permissions.dart';
import '../../core/problem.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../l10n/labels.dart';
import '../../models/admin_models.dart';
import '../../state/roles_provider.dart';
import '../../state/session_provider.dart';
import '../../theme/app_theme.dart';
import 'admin_widgets.dart';

/// The role/permission matrix (`GET /api/v1/admin/roles`, `GET /api/v1/admin/permissions`).
///
/// A role is nothing but a named set of permission codes, so the screen is a picker, a grouped catalogue of toggles,
/// and one save. The toggles are a local draft: `permissions` **replaces** the set on the server, so the screen can
/// only offer "the matrix is now this" — an incremental grant is not a thing the API has, and pretending otherwise
/// would make a revoked permission impossible to see.
///
/// The two permissions that cross account boundaries are marked wherever they appear, because that is what "this
/// role sees everyone's data" means in this product, and it is the one thing an administrator must not grant by
/// accident.
class RolesScreen extends StatefulWidget {
  /// Creates the screen.
  const RolesScreen({super.key});

  @override
  State<RolesScreen> createState() => _RolesScreenState();
}

class _RolesScreenState extends State<RolesScreen> {
  String? _selectedRoleId;

  /// The draft grant set for the selected role; null while it still matches what the server sent.
  Set<String>? _draft;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<RolesProvider>().load();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final canManage = context.watch<SessionProvider>().hasPermission(
      Permissions.userManage,
    );

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(
        title: Text(l10n.adminRolesTitle),
        actions: [
          if (canManage)
            IconButton(
              tooltip: l10n.adminRoleNew,
              onPressed: () => _showCreateDialog(context),
              icon: const Icon(Icons.add),
            ),
        ],
      ),
      body: AdminGate(
        permission: Permissions.userRead,
        child: Consumer<RolesProvider>(
          builder: (context, roles, _) {
            if (roles.roles == null) {
              if (roles.failure != null) {
                return SingleChildScrollView(
                  padding: const EdgeInsets.all(16),
                  child: AdminErrorView(
                    failure: roles.failure!,
                    onRetry: roles.load,
                  ),
                );
              }
              return const AdminLoading();
            }

            final list = roles.roles!;
            final selected = _selectedRole(list);

            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (roles.failure != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: AdminErrorView(
                      failure: roles.failure!,
                      onRetry: roles.load,
                    ),
                  ),
                _RolePicker(
                  roles: list,
                  selectedId: selected?.id,
                  onSelected: (role) => setState(() {
                    _selectedRoleId = role.id;
                    _draft = null;
                  }),
                ),
                const SizedBox(height: 12),
                if (selected == null)
                  AdminEmptyView(message: l10n.adminRolePick)
                else
                  _RoleMatrix(
                    role: selected,
                    catalogue: roles.catalogue ?? const [],
                    draft: _draft ?? selected.permissions.toSet(),
                    canManage: canManage && !roles.isSaving,
                    onToggle: (code, granted) => setState(() {
                      final draft = _draft ?? selected.permissions.toSet();
                      granted ? draft.add(code) : draft.remove(code);
                      _draft = draft;
                    }),
                    onSave: () => _save(selected),
                    onDelete: selected.isSystem
                        ? null
                        : () => _confirmDelete(selected),
                    onRevert: () => setState(() => _draft = null),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }

  RoleSummary? _selectedRole(List<RoleSummary> roles) {
    if (roles.isEmpty) {
      return null;
    }
    for (final role in roles) {
      if (role.id == _selectedRoleId) {
        return role;
      }
    }
    return roles.first;
  }

  Future<void> _save(RoleSummary role) async {
    final roles = context.read<RolesProvider>();
    final draft = _draft;

    if (draft == null) {
      return;
    }

    final failure = await roles.saveRole(role, permissions: draft);

    if (!mounted) {
      return;
    }

    setState(() => _draft = null);
    _report(context, failure, success: AppLocalizations.of(context).adminRoleSaved);
  }

  Future<void> _confirmDelete(RoleSummary role) async {
    final l10n = AppLocalizations.of(context);

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        backgroundColor: AppColors.bgCard,
        title: Text(l10n.adminRoleDeleteTitle(role.name)),
        content: Text(l10n.adminRoleDeleteHint),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: Text(l10n.cancel),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.statusDanger,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: Text(l10n.adminRoleDelete),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) {
      return;
    }

    final failure = await context.read<RolesProvider>().deleteRole(role);

    if (!mounted) {
      return;
    }

    setState(() {
      _selectedRoleId = null;
      _draft = null;
    });
    _report(context, failure, success: l10n.adminRoleDeleted);
  }

  Future<void> _showCreateDialog(BuildContext context) async {
    final roles = context.read<RolesProvider>();

    final result = await showDialog<_NewRoleResult>(
      context: context,
      builder: (_) => const _CreateRoleDialog(),
    );

    if (result == null) {
      return;
    }

    final failure = await roles.createRole(
      code: result.code,
      name: result.name,
      description: result.description,
      permissions: result.permissions,
    );

    if (!context.mounted) {
      return;
    }

    _report(context, failure, success: AppLocalizations.of(context).adminRoleCreated);
  }

  void _report(BuildContext context, AppFailure? failure, {required String success}) {
    final l10n = AppLocalizations.of(context);

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(failure == null ? success : failureMessage(l10n, failure)),
        backgroundColor: failure == null
            ? AppColors.bgCardHover
            : AppColors.statusDanger,
      ),
    );
  }
}

/// The role picker: one chip per role, with what each holds as a second line.
class _RolePicker extends StatelessWidget {
  const _RolePicker({
    required this.roles,
    required this.selectedId,
    required this.onSelected,
  });

  final List<RoleSummary> roles;
  final String? selectedId;
  final ValueChanged<RoleSummary> onSelected;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final role in roles)
          GestureDetector(
            onTap: () => onSelected(role),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
              decoration: BoxDecoration(
                color: role.id == selectedId
                    ? AppColors.primary.withValues(alpha: 0.20)
                    : AppColors.bgCard,
                borderRadius: BorderRadius.circular(14),
                border: Border.all(
                  color: role.id == selectedId
                      ? AppColors.primary
                      : AppColors.border,
                ),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Text(
                        role.name.isEmpty ? role.code : role.name,
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.bold,
                          color: AppColors.textMain,
                        ),
                      ),
                      const SizedBox(width: 6),
                      AdminStatusPill(
                        label: role.isSystem
                            ? l10n.adminRoleSystem
                            : l10n.adminRoleCustom,
                        color: role.isSystem
                            ? AppColors.textMuted
                            : AppColors.accent,
                      ),
                      if (role.crossesAccounts) ...[
                        const SizedBox(width: 6),
                        AdminStatusPill(
                          label: l10n.adminRoleCrossAccount,
                          color: AppColors.statusWarning,
                          icon: Icons.public,
                        ),
                      ],
                    ],
                  ),
                  const SizedBox(height: 2),
                  Text(
                    '${role.code} • ${l10n.adminRoleHolders(role.userCount)} • '
                    '${l10n.adminRoleGrants(role.permissions.length)}',
                    style: const TextStyle(
                      fontSize: 10,
                      color: AppColors.textMuted,
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

/// The matrix itself: the catalogue grouped by its own `group` field, with the draft's toggles.
class _RoleMatrix extends StatelessWidget {
  const _RoleMatrix({
    required this.role,
    required this.catalogue,
    required this.draft,
    required this.canManage,
    required this.onToggle,
    required this.onSave,
    required this.onDelete,
    required this.onRevert,
  });

  final RoleSummary role;
  final List<PermissionSummary> catalogue;
  final Set<String> draft;
  final bool canManage;
  final void Function(String code, bool granted) onToggle;
  final VoidCallback onSave;
  final VoidCallback? onDelete;
  final VoidCallback onRevert;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final groups = <String, List<PermissionSummary>>{};

    for (final permission in catalogue) {
      groups.putIfAbsent(permission.group, () => []).add(permission);
    }

    final isDirty =
        draft.length != role.permissions.length ||
        !draft.every(role.permissions.contains);
    final crossCount = draft
        .where(
          (code) => catalogue.any(
            (permission) => permission.code == code && permission.crossesAccounts,
          ),
        )
        .length;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AdminSection(
          title: role.name.isEmpty ? role.code : role.name,
          caption: role.description.isEmpty
              ? l10n.adminRoleCrossCaption
              : '${role.description}\n${l10n.adminRoleCrossCaption}',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              AdminKeyValue(
                label: l10n.adminRoleCode,
                value: role.code,
                monospace: true,
              ),
              AdminKeyValue(
                label: l10n.adminCountAccounts,
                value: l10n.adminRoleHolders(role.userCount),
              ),
              AdminKeyValue(
                label: l10n.adminRoleCrossAccount,
                value: l10n.adminRoleCrossCount(crossCount),
                valueColor: crossCount == 0
                    ? AppColors.textMuted
                    : AppColors.statusWarning,
              ),
              if (!canManage) ...[
                const SizedBox(height: 6),
                Text(
                  l10n.adminReadOnlyHint,
                  style: const TextStyle(
                    fontSize: 10,
                    color: AppColors.textMuted,
                  ),
                ),
              ],
            ],
          ),
        ),
        if (catalogue.isEmpty) AdminEmptyView(message: l10n.adminPermissionsEmpty),
        for (final group in groups.entries)
          AdminSection(
            title: permissionGroupLabel(l10n, group.key),
            child: Column(
              children: [
                for (final permission in group.value)
                  _PermissionTile(
                    permission: permission,
                    granted: draft.contains(permission.code),
                    enabled: canManage,
                    onToggle: (value) => onToggle(permission.code, value),
                  ),
              ],
            ),
          ),
        if (canManage)
          Row(
            children: [
              Expanded(
                child: Text(
                  isDirty ? l10n.adminRoleUnsaved : '',
                  style: const TextStyle(
                    fontSize: 11,
                    color: AppColors.statusWarning,
                  ),
                ),
              ),
              if (isDirty)
                TextButton(
                  onPressed: onRevert,
                  child: Text(l10n.adminRoleReload),
                ),
              const SizedBox(width: 8),
              ElevatedButton(
                onPressed: isDirty ? onSave : null,
                child: Text(l10n.save),
              ),
            ],
          ),
        if (onDelete != null)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: OutlinedButton.icon(
              style: OutlinedButton.styleFrom(
                foregroundColor: AppColors.statusDanger,
                side: BorderSide(
                  color: AppColors.statusDanger.withValues(alpha: 0.5),
                ),
              ),
              onPressed: onDelete,
              icon: const Icon(Icons.delete_outline, size: 16),
              label: Text(l10n.adminRoleDelete),
            ),
          ),
        const SizedBox(height: 24),
      ],
    );
  }
}

/// One permission, with its code, what it allows, and whether it reaches past the account.
class _PermissionTile extends StatelessWidget {
  const _PermissionTile({
    required this.permission,
    required this.granted,
    required this.enabled,
    required this.onToggle,
  });

  final PermissionSummary permission;
  final bool granted;
  final bool enabled;
  final ValueChanged<bool> onToggle;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final name = permissionNameLabel(l10n, permission.code, permission.name);
    final description = permissionDescriptionLabel(
      l10n,
      permission.code,
      permission.description,
    );

    return CheckboxListTile(
      value: granted,
      onChanged: enabled ? (value) => onToggle(value ?? false) : null,
      contentPadding: EdgeInsets.zero,
      controlAffinity: ListTileControlAffinity.leading,
      dense: true,
      title: Row(
        children: [
          Expanded(
            child: Text(
              name,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.bold,
                color: AppColors.textMain,
              ),
            ),
          ),
          if (permission.crossesAccounts)
            AdminStatusPill(
              label: l10n.adminRoleCrossAccount,
              color: AppColors.statusWarning,
              icon: Icons.public,
            ),
        ],
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            description,
            style: const TextStyle(
              fontSize: 10,
              height: 1.3,
              color: AppColors.textMuted,
            ),
          ),
          Text(
            permission.code,
            style: const TextStyle(
              fontSize: 10,
              fontFamily: 'monospace',
              color: AppColors.textMuted,
            ),
          ),
        ],
      ),
    );
  }
}

/// What the create dialog collects.
class _NewRoleResult {
  const _NewRoleResult({
    required this.code,
    required this.name,
    required this.description,
    required this.permissions,
  });

  final String code;
  final String name;
  final String description;
  final Set<String> permissions;
}

class _CreateRoleDialog extends StatefulWidget {
  const _CreateRoleDialog();

  @override
  State<_CreateRoleDialog> createState() => _CreateRoleDialogState();
}

class _CreateRoleDialogState extends State<_CreateRoleDialog> {
  final _code = TextEditingController();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final Set<String> _granted = {};

  @override
  void dispose() {
    _code.dispose();
    _name.dispose();
    _description.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final catalogue = context.watch<RolesProvider>().catalogue ?? const [];

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminRoleNew),
      content: SizedBox(
        width: 420,
        height: 420,
        child: Column(
          children: [
            TextField(
              controller: _code,
              decoration: InputDecoration(
                labelText: l10n.adminRoleCode,
                helperText: l10n.adminRoleRankHint,
                helperMaxLines: 2,
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _name,
              decoration: InputDecoration(labelText: l10n.adminRoleName),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _description,
              decoration: InputDecoration(labelText: l10n.adminRoleDescription),
            ),
            const SizedBox(height: 10),
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                l10n.adminRolePermissions,
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.bold,
                  color: AppColors.textMain,
                ),
              ),
            ),
            Expanded(
              child: ListView(
                children: [
                  for (final permission in catalogue)
                    CheckboxListTile(
                      value: _granted.contains(permission.code),
                      onChanged: (value) => setState(() {
                        value ?? false
                            ? _granted.add(permission.code)
                            : _granted.remove(permission.code);
                      }),
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      controlAffinity: ListTileControlAffinity.leading,
                      title: Text(
                        permissionNameLabel(
                          l10n,
                          permission.code,
                          permission.name,
                        ),
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMain,
                        ),
                      ),
                      subtitle: Text(
                        permission.code,
                        style: const TextStyle(
                          fontSize: 10,
                          fontFamily: 'monospace',
                          color: AppColors.textMuted,
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: Text(l10n.cancel),
        ),
        ElevatedButton(
          onPressed: () {
            if (_code.text.trim().isEmpty || _name.text.trim().isEmpty) {
              return;
            }
            Navigator.pop(
              context,
              _NewRoleResult(
                code: _code.text.trim(),
                name: _name.text.trim(),
                description: _description.text.trim(),
                permissions: _granted,
              ),
            );
          },
          child: Text(l10n.create),
        ),
      ],
    );
  }
}
