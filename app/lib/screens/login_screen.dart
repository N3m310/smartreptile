import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../core/problem.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../state/session_provider.dart';
import '../state/settings_provider.dart';
import '../theme/app_theme.dart';

/// The four authentication modes matching the web client (`Login.tsx`).
enum AuthMode { signIn, register, recover, reset }

/// Dedicated authentication and login screen matching the web VIVARIUMGUARD design.
///
/// Supports sign-in, account registration, recovery with single-use backup code,
/// reset with server-issued temporary code, bilingual language switching, and guest demo mode.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  AuthMode _mode = AuthMode.signIn;

  final _identifierController = TextEditingController();
  final _usernameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _newPasswordController = TextEditingController();
  final _codeController = TextEditingController();

  bool _showPassword = false;
  bool _showNewPassword = false;
  bool _isCopied = false;

  String? _notice;
  String? _issuedCode;
  AppFailure? _error;

  @override
  void dispose() {
    _identifierController.dispose();
    _usernameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _newPasswordController.dispose();
    _codeController.dispose();
    super.dispose();
  }

  void _clearFeedback() {
    setState(() {
      _error = null;
      _notice = null;
      _issuedCode = null;
      _isCopied = false;
    });
  }

  void _switchMode(AuthMode next) {
    _clearFeedback();
    _codeController.clear();
    _newPasswordController.clear();
    setState(() => _mode = next);
  }

  Future<void> _submit() async {
    final session = context.read<SessionProvider>();
    final l10n = AppLocalizations.of(context);
    final settings = context.read<SettingsProvider>();

    _clearFeedback();
    session.clearLastFailure();

    try {
      if (_mode == AuthMode.signIn) {
        final ok = await session.signIn(
          usernameOrEmail: _identifierController.text.trim(),
          password: _passwordController.text,
        );
        if (!ok && mounted) {
          setState(() {
            _error =
                session.lastFailure ??
                const AppFailure(code: 'invalid_credentials', statusCode: 401);
          });
        }
      } else if (_mode == AuthMode.register) {
        final username = _usernameController.text.trim();
        final recoveryCode = await session.register(
          username: username,
          email: _emailController.text.trim(),
          password: _passwordController.text,
          preferredLanguage: settings.locale?.languageCode ?? 'vi',
        );

        if (!mounted) return;
        setState(() {
          _issuedCode = recoveryCode;
          _notice = l10n.registerDone;
          _identifierController.text = username;
          _passwordController.clear();
          _mode = AuthMode.signIn;
        });
      } else if (_mode == AuthMode.recover) {
        final newRecoveryCode = await session.recover(
          usernameOrEmail: _identifierController.text.trim(),
          recoveryCode: _codeController.text.trim(),
          newPassword: _newPasswordController.text,
        );

        if (!mounted) return;
        setState(() {
          _issuedCode = newRecoveryCode;
          _notice = l10n.recoveryDone;
          _newPasswordController.clear();
          _codeController.clear();
          _mode = AuthMode.signIn;
        });
      } else {
        final newRecoveryCode = await session.resetPassword(
          usernameOrEmail: _identifierController.text.trim(),
          resetCode: _codeController.text.trim(),
          newPassword: _newPasswordController.text,
        );

        if (!mounted) return;
        setState(() {
          _issuedCode = newRecoveryCode;
          _notice = l10n.recoveryDone;
          _newPasswordController.clear();
          _codeController.clear();
          _mode = AuthMode.signIn;
        });
      }
    } on AppFailure catch (failure) {
      if (mounted) {
        setState(() => _error = failure);
      }
    } catch (_) {
      if (mounted) {
        setState(
          () =>
              _error = const AppFailure(code: 'errorGeneric', statusCode: 500),
        );
      }
    }
  }

  Future<void> _requestResetCode() async {
    final session = context.read<SessionProvider>();
    final l10n = AppLocalizations.of(context);

    final identifier = _identifierController.text.trim();
    if (identifier.isEmpty) return;

    _clearFeedback();

    try {
      await session.forgotPassword(usernameOrEmail: identifier);
      if (!mounted) return;
      setState(() {
        _notice = '${l10n.resetCodeRequested} ${l10n.resetCodeLogHint}';
        _mode = AuthMode.reset;
      });
    } on AppFailure catch (failure) {
      if (mounted) {
        setState(() => _error = failure);
      }
    } catch (_) {
      if (mounted) {
        setState(
          () =>
              _error = const AppFailure(code: 'errorGeneric', statusCode: 500),
        );
      }
    }
  }

  void _copyIssuedCode() {
    if (_issuedCode != null && _issuedCode!.isNotEmpty) {
      Clipboard.setData(ClipboardData(text: _issuedCode!));
      setState(() => _isCopied = true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Đã sao chép mã khôi phục vào bộ nhớ tạm'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionProvider>();
    final settings = context.watch<SettingsProvider>();
    final l10n = AppLocalizations.of(context);
    final isBusy = session.isBusy;

    final String heading = switch (_mode) {
      AuthMode.signIn => l10n.signInTitle,
      AuthMode.register => l10n.createAccount,
      AuthMode.recover => l10n.recoverButton,
      AuthMode.reset => l10n.resetCodeLabel,
    };

    final String actionLabel = switch (_mode) {
      AuthMode.register => isBusy ? l10n.registering : l10n.register,
      AuthMode.recover ||
      AuthMode.reset => isBusy ? l10n.recovering : l10n.recoverButton,
      AuthMode.signIn => isBusy ? l10n.signingIn : l10n.signIn,
    };

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Container(
                decoration: BoxDecoration(
                  color: AppColors.bgCard,
                  borderRadius: BorderRadius.circular(24),
                  border: Border.all(color: AppColors.border),
                  boxShadow: [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.5),
                      blurRadius: 28,
                      offset: const Offset(0, 10),
                    ),
                  ],
                ),
                padding: const EdgeInsets.all(24),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    // Brand Logo & Hero Header
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.center,
                      children: [
                        Container(
                          width: 48,
                          height: 48,
                          decoration: BoxDecoration(
                            gradient: const LinearGradient(
                              colors: [AppColors.primary, Color(0xFF204930)],
                              begin: Alignment.topLeft,
                              end: Alignment.bottomRight,
                            ),
                            borderRadius: BorderRadius.circular(16),
                            boxShadow: [
                              BoxShadow(
                                color: AppColors.primary.withValues(alpha: 0.3),
                                blurRadius: 16,
                                spreadRadius: 1,
                              ),
                            ],
                          ),
                          child: const Icon(
                            Icons.shield_outlined,
                            color: Colors.white,
                            size: 28,
                          ),
                        ),
                        const SizedBox(width: 14),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              RichText(
                                text: const TextSpan(
                                  style: TextStyle(
                                    fontSize: 22,
                                    fontWeight: FontWeight.w900,
                                    letterSpacing: 1.0,
                                    color: Colors.white,
                                  ),
                                  children: [
                                    TextSpan(text: 'VIVARIUM'),
                                    TextSpan(
                                      text: 'GUARD',
                                      style: TextStyle(
                                        color: AppColors.primary,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              const SizedBox(height: 2),
                              Text(
                                l10n.appTitle.toUpperCase(),
                                style: const TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.bold,
                                  letterSpacing: 2.0,
                                  color: AppColors.textMuted,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),

                    const SizedBox(height: 16),
                    Text(
                      _mode == AuthMode.register
                          ? l10n.registerPrompt
                          : l10n.signInPrompt,
                      style: const TextStyle(
                        fontSize: 13,
                        color: AppColors.textMuted,
                        height: 1.4,
                      ),
                    ),

                    const SizedBox(height: 16),
                    // Language Switcher Pills. A Wrap, not a Row: on a 360-411 dp handset the three children need
                    // more width than the card gives them, and a Row simply paints them past the edge (found by
                    // the fullstack E2E on a 411 dp emulator — 82 px past it).
                    Wrap(
                      spacing: 8,
                      runSpacing: 6,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Text(
                          l10n.language.toUpperCase(),
                          style: const TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.bold,
                            letterSpacing: 1.2,
                            color: AppColors.textMuted,
                          ),
                        ),
                        _LanguagePill(
                          identifier: 'login.lang.vi',
                          label: l10n.languageVi,
                          isSelected:
                              (settings.locale?.languageCode ??
                                  Localizations.localeOf(context)
                                      .languageCode) ==
                              'vi',
                          onTap: () => settings.setLocale(const Locale('vi')),
                        ),
                        _LanguagePill(
                          identifier: 'login.lang.en',
                          label: l10n.languageEn,
                          isSelected:
                              (settings.locale?.languageCode ??
                                  Localizations.localeOf(context)
                                      .languageCode) ==
                              'en',
                          onTap: () => settings.setLocale(const Locale('en')),
                        ),
                      ],
                    ),

                    const SizedBox(height: 20),
                    const Divider(color: AppColors.border, height: 1),
                    const SizedBox(height: 20),

                    // Heading
                    Text(
                      heading,
                      style: const TextStyle(
                        fontSize: 20,
                        fontWeight: FontWeight.bold,
                        color: AppColors.textMain,
                      ),
                    ),

                    // Error Alert Banner
                    if (_error != null) ...[
                      const SizedBox(height: 14),
                      Semantics(
                        identifier: 'login.error',
                        container: true,
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 14,
                            vertical: 12,
                          ),
                          decoration: BoxDecoration(
                            color: AppColors.statusDanger.withValues(
                              alpha: 0.12,
                            ),
                            borderRadius: BorderRadius.circular(12),
                            border: Border.all(
                              color: AppColors.statusDanger.withValues(
                                alpha: 0.5,
                              ),
                            ),
                          ),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const Icon(
                                Icons.error_outline,
                                color: AppColors.statusDanger,
                                size: 18,
                              ),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      failureMessage(l10n, _error!),
                                      style: const TextStyle(
                                        fontSize: 13,
                                        color: Colors.white,
                                        height: 1.3,
                                      ),
                                    ),
                                    if (_error!.errors.isNotEmpty)
                                      for (final err in _error!.errors)
                                        if (err is Map &&
                                            err['message'] != null)
                                          Padding(
                                            padding: const EdgeInsets.only(
                                              top: 4,
                                            ),
                                            child: Text(
                                              '• ${err['message']}',
                                              style: const TextStyle(
                                                fontSize: 12,
                                                color: AppColors.statusDanger,
                                              ),
                                            ),
                                          ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ],

                    // Notice Banner
                    if (_notice != null) ...[
                      const SizedBox(height: 14),
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 14,
                          vertical: 12,
                        ),
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: AppColors.primary.withValues(alpha: 0.5),
                          ),
                        ),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Icon(
                              Icons.check_circle_outline,
                              color: AppColors.primary,
                              size: 18,
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Text(
                                _notice!,
                                style: const TextStyle(
                                  fontSize: 13,
                                  color: Colors.white,
                                  height: 1.3,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],

                    // Issued Recovery Code Banner
                    if (_issuedCode != null) ...[
                      const SizedBox(height: 14),
                      Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: AppColors.bgMain,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(color: AppColors.primary),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              l10n.recoveryIssued,
                              style: const TextStyle(
                                fontSize: 12,
                                color: AppColors.textMuted,
                              ),
                            ),
                            const SizedBox(height: 6),
                            Row(
                              children: [
                                Expanded(
                                  child: SelectableText(
                                    _issuedCode!,
                                    style: const TextStyle(
                                      fontFamily: 'monospace',
                                      fontSize: 16,
                                      fontWeight: FontWeight.bold,
                                      letterSpacing: 1.5,
                                      color: AppColors.primary,
                                    ),
                                  ),
                                ),
                                IconButton(
                                  icon: Icon(
                                    _isCopied ? Icons.check : Icons.copy,
                                    color: _isCopied
                                        ? AppColors.primary
                                        : AppColors.textMuted,
                                    size: 20,
                                  ),
                                  tooltip: 'Sao chép mã',
                                  onPressed: _copyIssuedCode,
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],

                    const SizedBox(height: 18),

                    // Form Fields
                    if (_mode == AuthMode.register) ...[
                      _FormField(
                        identifier: 'login.username',
                        label: l10n.username,
                        icon: Icons.person_outline,
                        controller: _usernameController,
                        hintText: 'user.name',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.email',
                        label: l10n.email,
                        icon: Icons.mail_outline,
                        controller: _emailController,
                        hintText: 'name@example.com',
                        keyboardType: TextInputType.emailAddress,
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.password',
                        label: l10n.password,
                        icon: Icons.lock_outline,
                        controller: _passwordController,
                        obscureText: !_showPassword,
                        suffixIcon: IconButton(
                          icon: Icon(
                            _showPassword
                                ? Icons.visibility_off_outlined
                                : Icons.visibility_outlined,
                            color: AppColors.textMuted,
                            size: 20,
                          ),
                          onPressed: () =>
                              setState(() => _showPassword = !_showPassword),
                        ),
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l10n.passwordHint,
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                    ] else if (_mode == AuthMode.signIn) ...[
                      _FormField(
                        identifier: 'login.identifier',
                        label: l10n.usernameOrEmail,
                        icon: Icons.person_outline,
                        controller: _identifierController,
                        hintText: 'username / email',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.password',
                        label: l10n.password,
                        icon: Icons.lock_outline,
                        controller: _passwordController,
                        obscureText: !_showPassword,
                        suffixIcon: IconButton(
                          icon: Icon(
                            _showPassword
                                ? Icons.visibility_off_outlined
                                : Icons.visibility_outlined,
                            color: AppColors.textMuted,
                            size: 20,
                          ),
                          onPressed: () =>
                              setState(() => _showPassword = !_showPassword),
                        ),
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _submit(),
                      ),
                    ] else if (_mode == AuthMode.recover) ...[
                      _FormField(
                        identifier: 'login.identifier',
                        label: l10n.usernameOrEmail,
                        icon: Icons.person_outline,
                        controller: _identifierController,
                        hintText: 'username / email',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.code',
                        label: l10n.recoveryCode,
                        icon: Icons.vpn_key_outlined,
                        controller: _codeController,
                        hintText: 'XXXX-XXXX-XXXX-XXXX',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l10n.recoverHintBackup,
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.newPassword',
                        label: l10n.newPassword,
                        icon: Icons.lock_outline,
                        controller: _newPasswordController,
                        obscureText: !_showNewPassword,
                        suffixIcon: IconButton(
                          icon: Icon(
                            _showNewPassword
                                ? Icons.visibility_off_outlined
                                : Icons.visibility_outlined,
                            color: AppColors.textMuted,
                            size: 20,
                          ),
                          onPressed: () => setState(
                            () => _showNewPassword = !_showNewPassword,
                          ),
                        ),
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l10n.passwordHint,
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                    ] else ...[
                      // AuthMode.reset
                      _FormField(
                        identifier: 'login.identifier',
                        label: l10n.usernameOrEmail,
                        icon: Icons.person_outline,
                        controller: _identifierController,
                        hintText: 'username / email',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.resetCode',
                        label: l10n.resetCodeLabel,
                        icon: Icons.vpn_key_outlined,
                        controller: _codeController,
                        hintText: '123456',
                        textInputAction: TextInputAction.next,
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l10n.recoverHintIssued,
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                      const SizedBox(height: 16),
                      _FormField(
                        identifier: 'login.newPassword',
                        label: l10n.newPassword,
                        icon: Icons.lock_outline,
                        controller: _newPasswordController,
                        obscureText: !_showNewPassword,
                        suffixIcon: IconButton(
                          icon: Icon(
                            _showNewPassword
                                ? Icons.visibility_off_outlined
                                : Icons.visibility_outlined,
                            color: AppColors.textMuted,
                            size: 20,
                          ),
                          onPressed: () => setState(
                            () => _showNewPassword = !_showNewPassword,
                          ),
                        ),
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l10n.passwordHint,
                        style: const TextStyle(
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                    ],

                    const SizedBox(height: 24),

                    // Main Action Submit Button
                    SizedBox(
                      height: 48,
                      child: MergeSemantics(
                        child: Semantics(
                          identifier: 'login.submit',
                          child: ElevatedButton(
                            style: ElevatedButton.styleFrom(
                              backgroundColor: AppColors.primary,
                              foregroundColor: Colors.white,
                              elevation: 4,
                              shadowColor: AppColors.primary.withValues(
                                alpha: 0.4,
                              ),
                              shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(12),
                              ),
                            ),
                            onPressed: isBusy ? null : _submit,
                            child: isBusy
                                ? Row(
                                    mainAxisAlignment: MainAxisAlignment.center,
                                    children: [
                                      const SizedBox(
                                        width: 18,
                                        height: 18,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                          color: Colors.white,
                                        ),
                                      ),
                                      const SizedBox(width: 12),
                                      Text(
                                        actionLabel,
                                        style: const TextStyle(
                                          fontSize: 15,
                                          fontWeight: FontWeight.bold,
                                        ),
                                      ),
                                    ],
                                  )
                                : Row(
                                    mainAxisAlignment: MainAxisAlignment.center,
                                    children: [
                                      Text(
                                        actionLabel,
                                        style: const TextStyle(
                                          fontSize: 15,
                                          fontWeight: FontWeight.bold,
                                        ),
                                      ),
                                      const SizedBox(width: 8),
                                      const Icon(Icons.arrow_forward, size: 18),
                                    ],
                                  ),
                          ),
                        ),
                      ),
                    ),

                    const SizedBox(height: 20),

                    // Navigation / Switch Mode Links
                    if (_mode == AuthMode.signIn) ...[
                      Wrap(
                        alignment: WrapAlignment.spaceBetween,
                        runSpacing: 8,
                        children: [
                          TextButton(
                            onPressed: isBusy
                                ? null
                                : () => _switchMode(AuthMode.register),
                            child: Text(
                              l10n.createAccount,
                              style: const TextStyle(
                                color: AppColors.accent,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                          TextButton(
                            onPressed: isBusy
                                ? null
                                : () => _switchMode(AuthMode.recover),
                            child: Text(
                              l10n.forgotPassword,
                              style: const TextStyle(
                                color: AppColors.accent,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ] else ...[
                      Wrap(
                        alignment: WrapAlignment.spaceBetween,
                        runSpacing: 8,
                        children: [
                          if (_mode == AuthMode.recover)
                            TextButton(
                              onPressed:
                                  isBusy ||
                                      _identifierController.text.trim().isEmpty
                                  ? null
                                  : _requestResetCode,
                              child: Text(
                                l10n.sendResetCode,
                                style: TextStyle(
                                  color:
                                      _identifierController.text.trim().isEmpty
                                      ? AppColors.textMuted.withValues(
                                          alpha: 0.5,
                                        )
                                      : AppColors.accent,
                                  fontSize: 13,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                          TextButton(
                            onPressed: isBusy
                                ? null
                                : () => _switchMode(AuthMode.signIn),
                            child: Text(
                              l10n.backToSignIn,
                              style: const TextStyle(
                                color: AppColors.textMuted,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ],

                    const SizedBox(height: 16),
                    const Divider(color: AppColors.border, height: 1),
                    const SizedBox(height: 16),

                    // Guest / Demo exploration button. The identifier is what the E2E layer addresses it by
                    // (docs/04-quality/05 §7); MergeSemantics keeps the id and the button role on one node.
                    MergeSemantics(
                      child: Semantics(
                        identifier: 'login.guest',
                        child: OutlinedButton.icon(
                          style: OutlinedButton.styleFrom(
                            foregroundColor: AppColors.textMuted,
                            side: const BorderSide(color: AppColors.border),
                            padding: const EdgeInsets.symmetric(vertical: 12),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(12),
                            ),
                          ),
                          onPressed: () {
                            context.read<SessionProvider>().enterGuestMode();
                          },
                          icon: const Icon(Icons.explore_outlined, size: 18),
                          label: const Text(
                            'Khám phá bản thử nghiệm (Khách)',
                            style: TextStyle(fontSize: 13),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FormField extends StatelessWidget {
  const _FormField({
    required this.identifier,
    required this.label,
    required this.icon,
    required this.controller,
    this.hintText,
    this.obscureText = false,
    this.suffixIcon,
    this.keyboardType,
    this.textInputAction,
    this.onSubmitted,
  });

  /// The `Semantics` identifier the E2E layer addresses this field by (`docs/04-quality/05` §7).
  ///
  /// Required rather than optional: a field without one is a field no test can fill, and the failure would only
  /// show up as "the suite cannot reach this screen". It also gives TalkBack/VoiceOver an identity for the input.
  final String identifier;

  final String label;
  final IconData icon;
  final TextEditingController controller;
  final String? hintText;
  final bool obscureText;
  final Widget? suffixIcon;
  final TextInputType? keyboardType;
  final TextInputAction? textInputAction;
  final ValueChanged<String>? onSubmitted;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label.toUpperCase(),
          style: const TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.bold,
            letterSpacing: 1.2,
            color: AppColors.textMuted,
          ),
        ),
        const SizedBox(height: 8),
        // `label` as well as `identifier`: without a semantic name the field reads as an unnamed textbox to a
        // screen reader, and the drawn label above it is a separate node.
        Semantics(
          identifier: identifier,
          label: label,
          child: TextFormField(
            controller: controller,
            obscureText: obscureText,
            keyboardType: keyboardType,
            textInputAction: textInputAction,
            onFieldSubmitted: onSubmitted,
            style: const TextStyle(color: AppColors.textMain, fontSize: 14),
            decoration: InputDecoration(
              isDense: true,
              hintText: hintText,
              hintStyle: TextStyle(
                color: AppColors.textMuted.withValues(alpha: 0.5),
                fontSize: 13,
              ),
              prefixIcon: Icon(icon, color: AppColors.textMuted, size: 20),
              suffixIcon: suffixIcon,
              filled: true,
              fillColor: AppColors.bgInput,
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 14,
                vertical: 14,
              ),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(color: AppColors.border),
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(color: AppColors.border),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(
                  color: AppColors.primary,
                  width: 1.5,
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _LanguagePill extends StatelessWidget {
  const _LanguagePill({
    required this.identifier,
    required this.label,
    required this.isSelected,
    required this.onTap,
  });

  /// `Semantics` identifier, so the E2E layer switches language without matching copy in either language.
  final String identifier;

  final String label;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return MergeSemantics(
      child: Semantics(
        identifier: identifier,
        button: true,
        selected: isSelected,
        child: GestureDetector(
          onTap: onTap,
          behavior: HitTestBehavior.opaque,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              color: isSelected
                  ? AppColors.primary.withValues(alpha: 0.15)
                  : Colors.transparent,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(
                color: isSelected ? AppColors.primary : AppColors.border,
                width: isSelected ? 1.5 : 1.0,
              ),
            ),
            child: Text(
              label,
              style: TextStyle(
                fontSize: 12,
                fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
                color: isSelected ? AppColors.primary : AppColors.textMuted,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
