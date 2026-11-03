import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/problem.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../state/session_provider.dart';
import '../theme/app_theme.dart';

/// The account tab every role sees: who is signed in, the password change, and the way out. It is not behind a
/// permission because signing out and changing one's own password must be reachable by the owner, the technician
/// and the viewer alike.
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  final _formKey = GlobalKey<FormState>();
  final _currentCtrl = TextEditingController();
  final _newCtrl = TextEditingController();
  final _confirmCtrl = TextEditingController();
  bool _showPasswords = false;
  AppFailure? _error;
  bool _changed = false;

  @override
  void dispose() {
    _currentCtrl.dispose();
    _newCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    final session = context.read<SessionProvider>();
    setState(() {
      _error = null;
      _changed = false;
    });

    try {
      await session.changePassword(
        currentPassword: _currentCtrl.text,
        newPassword: _newCtrl.text,
      );
      if (!mounted) return;
      _currentCtrl.clear();
      _newCtrl.clear();
      _confirmCtrl.clear();
      setState(() => _changed = true);
    } on AppFailure catch (failure) {
      if (mounted) setState(() => _error = failure);
    } catch (_) {
      if (mounted) {
        setState(
          () =>
              _error = const AppFailure(code: 'errorGeneric', statusCode: 500),
        );
      }
    }
  }

  InputDecoration _decoration(String label) =>
      InputDecoration(labelText: label);

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionProvider>();
    final l10n = AppLocalizations.of(context);
    final user = session.user;

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: AppColors.bgCard,
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: AppColors.border),
          ),
          child: Row(
            children: [
              const CircleAvatar(
                radius: 26,
                backgroundColor: AppColors.primary,
                child: Icon(Icons.person, color: Colors.white, size: 28),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: user == null
                    ? Text(l10n.guestMode)
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            user.username,
                            style: const TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            user.email,
                            style: const TextStyle(
                              color: AppColors.textMuted,
                              fontSize: 13,
                            ),
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '${l10n.sessionRole}: ${user.role} • '
                            '${l10n.sessionPermissions(user.permissions.length)}',
                            style: const TextStyle(
                              color: AppColors.textMuted,
                              fontSize: 12,
                            ),
                          ),
                        ],
                      ),
              ),
            ],
          ),
        ),
        if (session.isSignedIn) ...[
          const SizedBox(height: 16),
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: AppColors.bgCard,
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: AppColors.border),
            ),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    l10n.changePassword,
                    style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    l10n.changePasswordPrompt,
                    style: const TextStyle(
                      color: AppColors.textMuted,
                      fontSize: 12,
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _currentCtrl,
                    obscureText: !_showPasswords,
                    autofillHints: const [AutofillHints.password],
                    decoration: _decoration(l10n.currentPassword),
                    validator: (value) => (value == null || value.isEmpty)
                        ? l10n.wrongCurrentPassword
                        : null,
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: _newCtrl,
                    obscureText: !_showPasswords,
                    autofillHints: const [AutofillHints.newPassword],
                    decoration: _decoration(l10n.newPassword).copyWith(
                      helperText: l10n.passwordHint,
                      helperMaxLines: 2,
                    ),
                    validator: (value) => (value == null || value.length < 8)
                        ? l10n.passwordHint
                        : null,
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: _confirmCtrl,
                    obscureText: !_showPasswords,
                    decoration: _decoration(l10n.confirmNewPassword),
                    validator: (value) =>
                        value != _newCtrl.text ? l10n.passwordMismatch : null,
                  ),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: () =>
                          setState(() => _showPasswords = !_showPasswords),
                      icon: Icon(
                        _showPasswords
                            ? Icons.visibility_off
                            : Icons.visibility,
                        size: 18,
                      ),
                      label: Text(
                        _showPasswords ? l10n.hidePassword : l10n.showPassword,
                      ),
                    ),
                  ),
                  if (_error != null) ...[
                    Text(
                      _error!.code == 'invalid_credentials'
                          ? l10n.wrongCurrentPassword
                          : failureMessage(l10n, _error!),
                      style: const TextStyle(
                        color: AppColors.statusDanger,
                        fontSize: 13,
                      ),
                    ),
                    for (final err in _error!.errors)
                      if (err is Map && err['message'] != null)
                        Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Text(
                            '• ${err['message']}',
                            style: const TextStyle(
                              color: AppColors.statusDanger,
                              fontSize: 12,
                            ),
                          ),
                        ),
                    const SizedBox(height: 8),
                  ],
                  if (_changed) ...[
                    Text(
                      l10n.passwordChanged,
                      style: const TextStyle(
                        color: AppColors.primary,
                        fontSize: 13,
                      ),
                    ),
                    const SizedBox(height: 8),
                  ],
                  FilledButton.icon(
                    onPressed: session.isBusy ? null : _submit,
                    icon: const Icon(Icons.lock_reset, size: 18),
                    label: Text(l10n.changePasswordSubmit),
                  ),
                ],
              ),
            ),
          ),
        ],
        const SizedBox(height: 16),
        SizedBox(
          height: 48,
          child: MergeSemantics(
            child: Semantics(
              identifier: 'profile.signOut',
              child: OutlinedButton.icon(
                onPressed: session.isBusy
                    ? null
                    : () async {
                        if (!session.isSignedIn) {
                          session.leaveGuestMode();
                          return;
                        }
                        await session.signOut();
                      },
                icon: Icon(
                  session.isSignedIn ? Icons.logout : Icons.login,
                  size: 18,
                ),
                label: Text(
                  session.isSignedIn ? l10n.signOut : l10n.signInOrLeaveGuest,
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
