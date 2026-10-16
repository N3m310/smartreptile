import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../models/session_models.dart';
import '../state/session_provider.dart';
import '../theme/app_theme.dart';

/// The account card on the settings screen: who is signed in, and what their codes allow.
///
/// It replaces the mock profile that used to sit there — a name and an email belonging to nobody — so the app has a
/// real place to sign in from. The card states the permission *count* rather than the role name because that is what
/// the app gates on: an administrator who wants to know why a door is missing reads the number and the codes behind
/// it, not the label, which a deployment may re-point at another set (`02-design/06` §3).
class SessionCard extends StatefulWidget {
  /// Creates the card.
  const SessionCard({super.key});

  @override
  State<SessionCard> createState() => _SessionCardState();
}

class _SessionCardState extends State<SessionCard> {
  final _identifier = TextEditingController();
  final _password = TextEditingController();
  bool _showPassword = false;

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = context.watch<SessionProvider>();

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.bgCard,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.border),
      ),
      child: session.isRestoring
          ? Row(
              children: [
                const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
                const SizedBox(width: 10),
                Text(
                  l10n.sessionRestoring,
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.textMuted,
                  ),
                ),
              ],
            )
          : session.isSignedIn
          ? _SignedIn(user: session.user!)
          : _SignInForm(
              identifier: _identifier,
              password: _password,
              showPassword: _showPassword,
              onToggleShowPassword: () =>
                  setState(() => _showPassword = !_showPassword),
              onSubmit: _submit,
            ),
    );
  }

  Future<void> _submit() async {
    final session = context.read<SessionProvider>();
    final l10n = AppLocalizations.of(context);

    final ok = await session.signIn(
      usernameOrEmail: _identifier.text.trim(),
      password: _password.text,
    );

    if (!mounted) {
      return;
    }

    if (!ok) {
      final failure = session.lastFailure;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            failure == null
                ? l10n.errorGeneric
                : failureMessage(l10n, failure),
          ),
          backgroundColor: AppColors.statusDanger,
        ),
      );
      return;
    }

    _password.clear();
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(l10n.signedInAs(session.user!.username)),
        backgroundColor: AppColors.bgCardHover,
      ),
    );
  }
}

class _SignedIn extends StatelessWidget {
  const _SignedIn({required this.user});

  final AccountProfile user;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return Row(
      children: [
        CircleAvatar(
          radius: 26,
          backgroundColor: AppColors.primary.withValues(alpha: 0.2),
          child: const Icon(
            Icons.person,
            color: AppColors.primary,
            size: 28,
          ),
        ),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                user.username,
                style: const TextStyle(
                  fontWeight: FontWeight.bold,
                  fontSize: 16,
                  color: AppColors.textMain,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                user.email,
                style: const TextStyle(
                  fontSize: 12,
                  color: AppColors.textMuted,
                ),
              ),
              const SizedBox(height: 6),
              Text(
                '${l10n.sessionRole}: ${user.role} • '
                '${l10n.sessionPermissions(user.permissions.length)}',
                style: const TextStyle(
                  fontSize: 11,
                  color: AppColors.textMuted,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _SignInForm extends StatelessWidget {
  const _SignInForm({
    required this.identifier,
    required this.password,
    required this.showPassword,
    required this.onToggleShowPassword,
    required this.onSubmit,
  });

  final TextEditingController identifier;
  final TextEditingController password;
  final bool showPassword;
  final VoidCallback onToggleShowPassword;
  final Future<void> Function() onSubmit;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final session = context.watch<SessionProvider>();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            const Icon(
              Icons.person_outline,
              color: AppColors.textMuted,
              size: 20,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    l10n.sessionSignedOut,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: AppColors.textMain,
                    ),
                  ),
                  Text(
                    l10n.sessionSignInPrompt,
                    style: const TextStyle(
                      fontSize: 11,
                      color: AppColors.textMuted,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        TextField(
          controller: identifier,
          autocorrect: false,
          decoration: InputDecoration(labelText: l10n.usernameOrEmail),
        ),
        const SizedBox(height: 10),
        TextField(
          controller: password,
          obscureText: !showPassword,
          onSubmitted: (_) => onSubmit(),
          decoration: InputDecoration(
            labelText: l10n.password,
            suffixIcon: IconButton(
              tooltip: showPassword ? l10n.hidePassword : l10n.showPassword,
              onPressed: onToggleShowPassword,
              icon: Icon(
                showPassword ? Icons.visibility_off : Icons.visibility,
                size: 18,
              ),
            ),
          ),
        ),
        const SizedBox(height: 12),
        SizedBox(
          width: double.infinity,
          height: 44,
          child: ElevatedButton.icon(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.primary,
              foregroundColor: Colors.white,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
            ),
            onPressed: session.isBusy ? null : () => onSubmit(),
            icon: session.isBusy
                ? const SizedBox(
                    width: 14,
                    height: 14,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Icon(Icons.login, size: 18),
            label: Text(
              session.isBusy ? l10n.signingIn : l10n.signIn,
              style: const TextStyle(fontWeight: FontWeight.bold),
            ),
          ),
        ),
      ],
    );
  }
}
