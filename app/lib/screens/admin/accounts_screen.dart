import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/formatting.dart';
import '../../core/paging.dart';
import '../../core/permissions.dart';
import '../../core/problem.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../l10n/labels.dart';
import '../../models/admin_models.dart';
import '../../state/accounts_provider.dart';
import '../../state/roles_provider.dart';
import '../../state/session_provider.dart';
import '../../theme/app_theme.dart';
import 'admin_widgets.dart';

/// Account management: the list, and every write `user.manage` allows (FR-18).
///
/// The actions are built from the session: a reader sees the same rows without the menu, because a button that can
/// only answer `403` is a worse answer than no button. The two deletes are offered side by side and the dialog says
/// which one keeps the history — a soft delete is the default the server applies, and an administrator who meant the
/// hard one has to say so.
class AccountsScreen extends StatefulWidget {
  /// Creates the screen.
  const AccountsScreen({super.key});

  @override
  State<AccountsScreen> createState() => _AccountsScreenState();
}

class _AccountsScreenState extends State<AccountsScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        context.read<AccountsProvider>().loadAccounts();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      appBar: AppBar(
        title: Text(l10n.adminAccountsTitle),
        actions: [
          if (context.watch<SessionProvider>().hasPermission(
            Permissions.userManage,
          ))
            IconButton(
              tooltip: l10n.adminAccountNew,
              onPressed: () => _showCreateDialog(context),
              icon: const Icon(Icons.person_add_alt),
            ),
        ],
      ),
      body: AdminGate(
        permission: Permissions.userRead,
        child: Consumer<AccountsProvider>(
          builder: (context, accounts, _) {
            final page = accounts.accounts;

            if (page == null) {
              return const AdminLoading();
            }

            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                AdminSearchField(
                  hint: l10n.adminAccountsSearch,
                  onSubmitted: (value) => accounts.loadAccounts(search: value),
                ),
                const SizedBox(height: 12),
                if (page.failure != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: AdminErrorView(
                      failure: page.failure!,
                      onRetry: () => accounts.loadAccounts(skip: page.skip),
                    ),
                  ),
                if (page.isLoading) const AdminLoading(),
                if (!page.isLoading &&
                    page.items.isEmpty &&
                    page.failure == null)
                  AdminEmptyView(message: l10n.adminNoRows),
                for (final account in page.items) _AccountCard(account: account),
                if (page.items.isNotEmpty)
                  AdminPager(
                    state: page,
                    onPrevious: () =>
                        accounts.loadAccounts(skip: page.skip - adminPageSize),
                    onNext: () =>
                        accounts.loadAccounts(skip: page.skip + adminPageSize),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }

  Future<void> _showCreateDialog(BuildContext context) async {
    final accounts = context.read<AccountsProvider>();
    final roles = context.read<RolesProvider>();

    // The dialog offers the deployment's own roles, so a custom role is assignable at creation. The catalogue is
    // not needed for that, and asking for it would be one request more than the dialog can use.
    await roles.load(includeCatalogue: false);

    if (!context.mounted) {
      return;
    }

    final result = await showDialog<_CreateAccountResult>(
      context: context,
      builder: (_) => _CreateAccountDialog(
        roles: roles.roles?.map((role) => role.code).toList() ?? const [],
      ),
    );

    if (result == null) {
      return;
    }

    final failure = await accounts.createAccount(
      username: result.username,
      email: result.email,
      password: result.password,
      role: result.role,
      preferredLanguage: result.preferredLanguage,
    );

    if (!context.mounted) {
      return;
    }

    _report(context, failure, success: AppLocalizations.of(context).adminAccountCreated);
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

/// One account row: who it is, what it may do, and what an administrator can do to it.
class _AccountCard extends StatelessWidget {
  const _AccountCard({required this.account});

  final AccountSummary account;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final canManage = context.watch<SessionProvider>().hasPermission(
      Permissions.userManage,
    );

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  account.username,
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.bold,
                    color: AppColors.textMain,
                  ),
                ),
              ),
              AdminStatusPill(
                label: account.isActive
                    ? l10n.adminBadgeActive
                    : l10n.adminBadgeDisabled,
                color: account.isActive
                    ? AppColors.statusNormal
                    : AppColors.statusOffline,
                icon: account.isActive ? Icons.check_circle_outline : Icons.block,
              ),
              if (canManage)
                PopupMenuButton<String>(
                  tooltip: l10n.adminAccountActions,
                  icon: const Icon(
                    Icons.more_vert,
                    size: 18,
                    color: AppColors.textMuted,
                  ),
                  onSelected: (action) =>
                      _handle(context, action, account),
                  itemBuilder: (context) => [
                    PopupMenuItem(
                      value: 'edit',
                      child: Text(l10n.adminAccountEdit),
                    ),
                    PopupMenuItem(
                      value: 'role',
                      child: Text(l10n.adminAccountChangeRole),
                    ),
                    PopupMenuItem(
                      value: 'password',
                      child: Text(l10n.adminAccountResetPassword),
                    ),
                    PopupMenuItem(
                      value: account.isActive ? 'disable' : 'enable',
                      child: Text(
                        account.isActive
                            ? l10n.adminAccountDisable
                            : l10n.adminAccountEnable,
                      ),
                    ),
                    PopupMenuItem(
                      value: 'delete',
                      child: Text(l10n.adminAccountDelete),
                    ),
                  ],
                ),
            ],
          ),
          Text(
            account.email,
            style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
          ),
          const SizedBox(height: 8),
          AdminKeyValue(
            label: l10n.adminColumnRole,
            value: account.role,
            monospace: true,
          ),
          AdminKeyValue(
            label: l10n.sessionPermissionsLabel,
            value: l10n.sessionPermissions(account.permissions.length),
          ),
          AdminKeyValue(
            label: l10n.adminColumnCreated,
            value: account.createdAt == null
                ? l10n.never
                : MetricFormat.dateTime(account.createdAt!),
          ),
          AdminKeyValue(
            label: l10n.adminColumnLastLogin,
            value: account.lastLoginAt == null
                ? l10n.never
                : MetricFormat.dateTime(account.lastLoginAt!),
          ),
        ],
      ),
    );
  }

  Future<void> _handle(
    BuildContext context,
    String action,
    AccountSummary account,
  ) async {
    final accounts = context.read<AccountsProvider>();
    final session = context.read<SessionProvider>();
    final l10n = AppLocalizations.of(context);

    AppFailure? failure;
    String? confirmation;

    switch (action) {
      case 'edit':
        final patch = await showDialog<_EditAccountResult>(
          context: context,
          builder: (_) => _EditAccountDialog(account: account),
        );
        if (patch == null) {
          return;
        }
        failure = await accounts.updateAccount(
          account.id,
          username: patch.username,
          email: patch.email,
          preferredLanguage: patch.preferredLanguage,
          minNotifySeverity: patch.minNotifySeverity,
          quietHoursStart: patch.quietHoursStart,
          quietHoursEnd: patch.quietHoursEnd,
        );
        confirmation = l10n.adminAccountSaved;
      case 'role':
        // The picker offers the deployment's own roles, so a custom one is assignable; it needs the role list, which
        // the accounts screen has no other reason to hold.
        await context.read<RolesProvider>().load(includeCatalogue: false);

        if (!context.mounted) {
          return;
        }

        final role = await showDialog<String>(
          context: context,
          builder: (_) => _ChangeRoleDialog(current: account.role),
        );
        if (role == null) {
          return;
        }
        failure = await accounts.changeRole(account.id, role);
        confirmation = l10n.adminRoleAssigned;
      case 'password':
        final password = await showDialog<String>(
          context: context,
          builder: (_) => const _ResetPasswordDialog(),
        );
        if (password == null) {
          return;
        }
        failure = await accounts.resetPassword(account.id, password);
        confirmation = l10n.adminPasswordReset;
      case 'disable':
      case 'enable':
        final disabled = action == 'disable';
        failure = await accounts.setDisabled(account.id, disabled: disabled);
        confirmation = disabled
            ? l10n.adminAccountDisabled
            : l10n.adminAccountEnabled;
      case 'delete':
        final purge = await showDialog<bool>(
          context: context,
          builder: (_) => _DeleteAccountDialog(account: account),
        );
        if (purge == null) {
          return;
        }
        failure = await accounts.deleteAccount(account.id, purge: purge);
        confirmation = l10n.adminAccountRemoved;
      default:
        return;
    }

    if (!context.mounted) {
      return;
    }

    if (failure != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(failureMessage(l10n, failure)),
          backgroundColor: AppColors.statusDanger,
        ),
      );
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(confirmation),
        backgroundColor: AppColors.bgCardHover,
      ),
    );

    // An administrator can change their own account, so the session's copy of the profile may now be stale.
    await session.refreshProfile();
  }
}

/// What the create dialog collects.
class _CreateAccountResult {
  const _CreateAccountResult({
    required this.username,
    required this.email,
    required this.password,
    required this.role,
    required this.preferredLanguage,
  });

  final String username;
  final String email;
  final String password;
  final String role;
  final String preferredLanguage;
}

class _CreateAccountDialog extends StatefulWidget {
  const _CreateAccountDialog({required this.roles});

  final List<String> roles;

  @override
  State<_CreateAccountDialog> createState() => _CreateAccountDialogState();
}

class _CreateAccountDialogState extends State<_CreateAccountDialog> {
  final _username = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  late String _role = widget.roles.contains(SystemRoleCodes.owner)
      ? SystemRoleCodes.owner
      : widget.roles.first;
  String _language = 'vi';

  @override
  void dispose() {
    _username.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final roles = widget.roles.isEmpty ? SystemRoleCodes.all : widget.roles;

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminAccountNew),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _username,
              decoration: InputDecoration(labelText: l10n.username),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _email,
              decoration: InputDecoration(labelText: l10n.email),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _password,
              obscureText: true,
              decoration: InputDecoration(
                labelText: l10n.password,
                helperText: l10n.passwordHint,
                helperMaxLines: 2,
              ),
            ),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: _role,
              decoration: InputDecoration(labelText: l10n.adminFormRole),
              items: [
                for (final role in roles)
                  DropdownMenuItem(value: role, child: Text(role)),
              ],
              onChanged: (value) => setState(() => _role = value ?? _role),
            ),
            const SizedBox(height: 10),
            _LanguageField(
              value: _language,
              onChanged: (value) => setState(() => _language = value),
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
            if (_username.text.trim().isEmpty ||
                _email.text.trim().isEmpty ||
                _password.text.isEmpty) {
              return;
            }
            Navigator.pop(
              context,
              _CreateAccountResult(
                username: _username.text.trim(),
                email: _email.text.trim(),
                password: _password.text,
                role: _role,
                preferredLanguage: _language,
              ),
            );
          },
          child: Text(l10n.create),
        ),
      ],
    );
  }
}

/// What the edit dialog collects. Only the members the account screen owns: the time zone belongs to the account.
class _EditAccountResult {
  const _EditAccountResult({
    required this.username,
    required this.email,
    required this.preferredLanguage,
    required this.minNotifySeverity,
    required this.quietHoursStart,
    required this.quietHoursEnd,
  });

  final String username;
  final String email;
  final String preferredLanguage;
  final String minNotifySeverity;
  final String quietHoursStart;
  final String quietHoursEnd;
}

class _EditAccountDialog extends StatefulWidget {
  const _EditAccountDialog({required this.account});

  final AccountSummary account;

  @override
  State<_EditAccountDialog> createState() => _EditAccountDialogState();
}

class _EditAccountDialogState extends State<_EditAccountDialog> {
  late final _username = TextEditingController(text: widget.account.username);
  late final _email = TextEditingController(text: widget.account.email);
  late final _quietStart = TextEditingController(
    text: widget.account.quietHoursStart ?? '',
  );
  late final _quietEnd = TextEditingController(
    text: widget.account.quietHoursEnd ?? '',
  );
  late String _language = widget.account.preferredLanguage;
  late String _severity = widget.account.minNotifySeverity;

  @override
  void dispose() {
    _username.dispose();
    _email.dispose();
    _quietStart.dispose();
    _quietEnd.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminAccountEdit),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _username,
              decoration: InputDecoration(labelText: l10n.username),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _email,
              decoration: InputDecoration(labelText: l10n.email),
            ),
            const SizedBox(height: 10),
            _LanguageField(
              value: _language,
              onChanged: (value) => setState(() => _language = value),
            ),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: _severity,
              decoration: InputDecoration(
                labelText: l10n.adminFormMinNotifySeverity,
              ),
              items: const [
                DropdownMenuItem(value: 'info', child: Text('info')),
                DropdownMenuItem(value: 'warning', child: Text('warning')),
                DropdownMenuItem(value: 'critical', child: Text('critical')),
              ],
              onChanged: (value) => setState(() => _severity = value ?? _severity),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _quietStart,
              decoration: InputDecoration(
                labelText: l10n.adminFormQuietHoursStart,
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _quietEnd,
              decoration: InputDecoration(
                labelText: l10n.adminFormQuietHoursEnd,
                helperText: l10n.adminFormQuietHoursHint,
                helperMaxLines: 2,
              ),
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                const Icon(
                  Icons.schedule,
                  size: 14,
                  color: AppColors.textMuted,
                ),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    l10n.adminFormTimeZoneNote,
                    style: const TextStyle(
                      fontSize: 10,
                      color: AppColors.textMuted,
                    ),
                  ),
                ),
              ],
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
          onPressed: () => Navigator.pop(
            context,
            _EditAccountResult(
              username: _username.text.trim(),
              email: _email.text.trim(),
              preferredLanguage: _language,
              minNotifySeverity: _severity,
              quietHoursStart: _quietStart.text.trim(),
              quietHoursEnd: _quietEnd.text.trim(),
            ),
          ),
          child: Text(l10n.save),
        ),
      ],
    );
  }
}

class _ChangeRoleDialog extends StatefulWidget {
  const _ChangeRoleDialog({required this.current});

  final String current;

  @override
  State<_ChangeRoleDialog> createState() => _ChangeRoleDialogState();
}

class _ChangeRoleDialogState extends State<_ChangeRoleDialog> {
  late String _role = widget.current;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final roles = context.watch<RolesProvider>().roles;
    final codes = roles == null || roles.isEmpty
        ? SystemRoleCodes.all
        : [for (final role in roles) role.code];

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminAccountChangeRole),
      content: DropdownButtonFormField<String>(
        initialValue: codes.contains(_role) ? _role : codes.first,
        decoration: InputDecoration(labelText: l10n.adminFormRole),
        items: [
          for (final code in codes)
            DropdownMenuItem(value: code, child: Text(code)),
        ],
        onChanged: (value) => setState(() => _role = value ?? _role),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: Text(l10n.cancel),
        ),
        ElevatedButton(
          onPressed: () => Navigator.pop(context, _role),
          child: Text(l10n.save),
        ),
      ],
    );
  }
}

class _ResetPasswordDialog extends StatefulWidget {
  const _ResetPasswordDialog();

  @override
  State<_ResetPasswordDialog> createState() => _ResetPasswordDialogState();
}

class _ResetPasswordDialogState extends State<_ResetPasswordDialog> {
  final _password = TextEditingController();

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminAccountResetPassword),
      content: TextField(
        controller: _password,
        obscureText: true,
        decoration: InputDecoration(
          labelText: l10n.adminNewPassword,
          helperText: l10n.passwordHint,
          helperMaxLines: 2,
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: Text(l10n.cancel),
        ),
        ElevatedButton(
          onPressed: _password.text.isEmpty
              ? null
              : () => Navigator.pop(context, _password.text),
          child: Text(l10n.save),
        ),
      ],
    );
  }
}

/// The delete dialog: both modes, explained, and the harder one second.
class _DeleteAccountDialog extends StatefulWidget {
  const _DeleteAccountDialog({required this.account});

  final AccountSummary account;

  @override
  State<_DeleteAccountDialog> createState() => _DeleteAccountDialogState();
}

class _DeleteAccountDialogState extends State<_DeleteAccountDialog> {
  bool _purge = false;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return AlertDialog(
      backgroundColor: AppColors.bgCard,
      title: Text(l10n.adminAccountDeleteTitle(widget.account.username)),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Not a `RadioListTile`: the two modes have to be read, and two labelled tiles state the difference
            // without depending on a group value the framework deprecated.
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(
                _purge ? Icons.radio_button_unchecked : Icons.radio_button_checked,
                size: 18,
                color: AppColors.primary,
              ),
              title: Text(l10n.adminAccountDeleteSoft),
              subtitle: Text(
                l10n.adminAccountDeleteSoftHint,
                style: const TextStyle(fontSize: 11),
              ),
              onTap: () => setState(() => _purge = false),
            ),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(
                _purge ? Icons.radio_button_checked : Icons.radio_button_unchecked,
                size: 18,
                color: AppColors.statusDanger,
              ),
              title: Text(l10n.adminAccountDeleteHard),
              subtitle: Text(
                l10n.adminAccountDeleteHardHint,
                style: const TextStyle(fontSize: 11),
              ),
              onTap: () => setState(() => _purge = true),
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
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.statusDanger,
            foregroundColor: Colors.white,
          ),
          onPressed: () => Navigator.pop(context, _purge),
          child: Text(l10n.adminAccountDelete),
        ),
      ],
    );
  }
}

/// The language selector, shared by the create and edit dialogs.
class _LanguageField extends StatelessWidget {
  const _LanguageField({required this.value, required this.onChanged});

  final String value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return DropdownButtonFormField<String>(
      initialValue: value.isEmpty ? 'vi' : value,
      decoration: InputDecoration(labelText: l10n.adminFormPreferredLanguage),
      items: [
        DropdownMenuItem(value: 'vi', child: Text(l10n.languageVi)),
        DropdownMenuItem(value: 'en', child: Text(l10n.languageEn)),
      ],
      onChanged: (selected) => onChanged(selected ?? value),
    );
  }
}
