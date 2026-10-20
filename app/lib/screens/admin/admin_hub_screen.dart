import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/permissions.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../models/session_models.dart';
import '../../state/session_provider.dart';
import '../../theme/app_theme.dart';
import 'accounts_screen.dart';
import 'admin_overview_screen.dart';
import 'admin_widgets.dart';
import 'admin_lists_screen.dart';
import 'roles_screen.dart';

/// The administration landing page: one card per surface the session may open.
///
/// The cards are built from the session's permission codes, so a keeper who holds none of them sees a sentence
/// instead of four doors that would each answer `403`. That is the same rule the shell follows when it decides
/// whether the tab exists at all.
class AdminHubScreen extends StatelessWidget {
  /// Creates the hub.
  const AdminHubScreen({super.key});

  /// The four surfaces, each with the permission that opens it.
  static final List<_AdminEntry> _entries = [
    _AdminEntry(
      permission: Permissions.dataReadAny,
      icon: Icons.insights_outlined,
      title: (l10n) => l10n.adminOverviewEntry,
      build: () => const AdminOverviewScreen(),
    ),
    _AdminEntry(
      permission: Permissions.dataReadAny,
      icon: Icons.grid_view_outlined,
      title: (l10n) => l10n.adminListsEntry,
      build: () => const AdminListsScreen(),
    ),
    _AdminEntry(
      permission: Permissions.userRead,
      icon: Icons.manage_accounts_outlined,
      title: (l10n) => l10n.adminAccountsEntry,
      build: () => const AccountsScreen(),
    ),
    _AdminEntry(
      permission: Permissions.userRead,
      icon: Icons.admin_panel_settings_outlined,
      title: (l10n) => l10n.adminRolesEntry,
      build: () => const RolesScreen(),
    ),
  ];

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionProvider>();
    final available = [
      for (final entry in _entries)
        if (session.hasPermission(entry.permission)) entry,
    ];

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        if (session.user != null) _SessionSummary(user: session.user!),
        const SizedBox(height: 4),
        if (available.isEmpty)
          const AdminNotPermitted()
        else
          for (final entry in available)
            _EntryCard(
              entry: entry,
              onOpen: () => Navigator.push(
                context,
                MaterialPageRoute(builder: (_) => entry.build()),
              ),
            ),
      ],
    );
  }
}

/// One surface, as a card.
class _EntryCard extends StatelessWidget {
  const _EntryCard({required this.entry, required this.onOpen});

  final _AdminEntry entry;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.border),
      ),
      child: InkWell(
        onTap: onOpen,
        borderRadius: BorderRadius.circular(20),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: AppColors.primary.withValues(alpha: 0.16),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Icon(entry.icon, size: 20, color: AppColors.primary),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  entry.title(l10n),
                  style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
              const Icon(
                Icons.chevron_right,
                color: AppColors.textMuted,
                size: 18,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Who is signed in and what their codes allow, so "why can I not see that" is answerable on the spot.
class _SessionSummary extends StatelessWidget {
  const _SessionSummary({required this.user});

  final AccountProfile user;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AdminSection(
      title: l10n.sessionTitle,
      caption: null,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AdminKeyValue(label: l10n.username, value: user.username),
          AdminKeyValue(label: l10n.email, value: user.email),
          AdminKeyValue(
            label: l10n.sessionRole,
            value: user.role,
            monospace: true,
          ),
          AdminKeyValue(
            label: l10n.sessionPermissionsLabel,
            value: l10n.sessionPermissions(user.permissions.length),
          ),
        ],
      ),
    );
  }
}

/// A surface and the permission that opens it.
class _AdminEntry {
  const _AdminEntry({
    required this.permission,
    required this.icon,
    required this.title,
    required this.build,
  });

  final String permission;
  final IconData icon;
  final String Function(AppLocalizations l10n) title;
  final Widget Function() build;
}
