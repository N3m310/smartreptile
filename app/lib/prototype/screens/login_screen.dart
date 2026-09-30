/// Sign in, sign up and the "splash" decision, on one screen (S1–S3).
///
/// The three states share a screen because they share everything else: the same validation, the same error surface,
/// the same demo note. Splitting them would triple the code to show the reader three almost-identical layouts.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../labels.dart';
import '../prototype_state.dart';
import '../widgets/prototype_ui.dart';

/// S1–S3: the unauthenticated entry point.
class LoginScreen extends StatefulWidget {
  /// Creates the screen.
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController(text: PrototypeState.demoEmail);
  final _password = TextEditingController(text: PrototypeState.demoPassword);
  final _name = TextEditingController();
  final _formKey = GlobalKey<FormState>();

  bool _isRegistering = false;
  bool _staySignedIn = true;
  String _error = '';

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _name.dispose();
    super.dispose();
  }

  void _submit() {
    final state = context.read<PrototypeState>();
    final error = _isRegistering
        ? state.register(
            name: _name.text,
            email: _email.text,
            password: _password.text,
          )
        : state.signIn(email: _email.text, password: _password.text);

    setState(() => _error = error);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Icon(
                        Icons.terrain_outlined,
                        size: 32,
                        color: theme.colorScheme.primary,
                      ),
                      const SizedBox(width: 10),
                      Text(
                        Labels.appTitle,
                        style: theme.textTheme.headlineSmall?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    _isRegistering
                        ? Labels.registerTitle
                        : Labels.loginSubtitle,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 20),
                  if (_isRegistering) ...[
                    TextField(
                      controller: _name,
                      textInputAction: TextInputAction.next,
                      decoration: const InputDecoration(
                        labelText: Labels.fieldName,
                        border: OutlineInputBorder(),
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],
                  TextField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: Labels.fieldEmail,
                      border: OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _password,
                    obscureText: true,
                    onSubmitted: (_) => _submit(),
                    decoration: const InputDecoration(
                      labelText: Labels.fieldPassword,
                      border: OutlineInputBorder(),
                    ),
                  ),
                  if (!_isRegistering) ...[
                    const SizedBox(height: 4),
                    SwitchListTile.adaptive(
                      value: _staySignedIn,
                      onChanged: (value) =>
                          setState(() => _staySignedIn = value),
                      title: const Text(Labels.loginStaySignedIn),
                      contentPadding: EdgeInsets.zero,
                      dense: true,
                    ),
                  ],
                  if (_error.isNotEmpty) ...[
                    const SizedBox(height: 8),
                    Callout(
                      message: _error,
                      color: ProtoColors.critical,
                      icon: Icons.error_outline,
                      dense: true,
                    ),
                  ],
                  const SizedBox(height: 16),
                  FilledButton(
                    onPressed: _submit,
                    child: Text(
                      _isRegistering
                          ? Labels.registerAction
                          : Labels.loginAction,
                    ),
                  ),
                  TextButton(
                    onPressed: () => setState(() {
                      _isRegistering = !_isRegistering;
                      _error = '';
                    }),
                    child: Text(
                      _isRegistering
                          ? Labels.registerToLogin
                          : Labels.loginToRegister,
                    ),
                  ),
                  const SizedBox(height: 8),
                  const Callout(
                    message: Labels.loginDemoNote,
                    color: ProtoColors.prototype,
                    icon: Icons.science_outlined,
                    title: Labels.prototypeTitle,
                    dense: true,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
