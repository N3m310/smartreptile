import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'app.dart';
import 'core/clock.dart';
import 'core/env.dart';
import 'data/api_client.dart';
import 'prototype/prototype_shell.dart';
import 'prototype/prototype_state.dart';
import 'state/accounts_provider.dart';
import 'state/admin_provider.dart';
import 'state/app_data_provider.dart';
import 'state/roles_provider.dart';
import 'state/session_provider.dart';
import 'state/settings_provider.dart';
import 'state/telemetry_provider.dart';

/// Composition root of the app (`03-implementation/05` §1).
///
/// Providers are created once here and are not rebuilt from the widget tree, so a settings change or a token
/// refresh cannot recreate every screen and lose scroll position.
///
/// Two modes live side by side:
///  * **the app** (the default) is the VIVARIUMGUARD shell in `app.dart` — the client a keeper sees;
///  * **the prototype** (`--dart-define=PROTOTYPE=true`) renders its own screens against generated fake data, with
///    every threshold, dwell and suppression decision made by the real rule kernel in `lib/prototype/rules/`.
///
/// Both are built and only one is shown, so the prototype cannot quietly rot away from the data layer it imitates.
/// The default was the prototype until 2026-10-14: it was the richer UI while the API had nothing to say, and it
/// became the wrong first impression the moment the app had a shell of its own with the current brand and copy
/// deck.
void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final clock = Clock.system();
  final api = ApiClient(baseUrl: Env.apiBase);
  final settings = SettingsProvider();
  final appData = AppDataProvider();
  final session = SessionProvider(api: api);
  final admin = AdminProvider(api: api);
  final accounts = AccountsProvider(api: api);
  final roles = RolesProvider(api: api);

  // One listener rather than four calls at every sign-out site: everything the administration surfaces hold belongs
  // to the account that was signed in, so the moment the session ends they are emptied. A screen left behind would
  // otherwise be showing the deployment's data to whoever picks the phone up next.
  session.addListener(() {
    if (!session.isSignedIn) {
      admin.clear();
      accounts.clear();
      roles.clear();
    }
  });

  runApp(
    MultiProvider(
      providers: [
        Provider<Clock>.value(value: clock),
        Provider<ApiClient>.value(value: api),
        ChangeNotifierProvider<SettingsProvider>.value(value: settings),
        ChangeNotifierProvider<AppDataProvider>.value(value: appData),
        ChangeNotifierProvider<SessionProvider>.value(value: session),
        ChangeNotifierProvider<AdminProvider>.value(value: admin),
        ChangeNotifierProvider<AccountsProvider>.value(value: accounts),
        ChangeNotifierProvider<RolesProvider>.value(value: roles),
        ChangeNotifierProvider<TelemetryProvider>(
          create: (_) => TelemetryProvider(api: api, clock: clock),
        ),
        if (Env.prototypeMode)
          ChangeNotifierProvider<PrototypeState>(
            // The fake world is generated once from the injected clock and then mutated in memory. Nothing on this
            // path touches the network or the disk.
            create: (_) => PrototypeState(clock: clock),
          ),
      ],
      child: Env.prototypeMode ? const PrototypeApp() : const SmartReptileApp(),
    ),
  );

  // Fire-and-forget: preferences only affect the first frame's theme, and blocking launch on disk I/O is worse
  // than a one-frame system-theme flash.
  unawaited(settings.load());

  // The stored refresh token, on the other hand, decides whether the administration tab exists at all — so the
  // shell shows its restoring state until this answers, rather than flashing a signed-out navigation bar.
  unawaited(session.restore());
}
