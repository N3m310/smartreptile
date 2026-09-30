import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'app.dart';
import 'core/clock.dart';
import 'core/env.dart';
import 'data/api_client.dart';
import 'prototype/prototype_shell.dart';
import 'prototype/prototype_state.dart';
import 'state/settings_provider.dart';
import 'state/telemetry_provider.dart';

/// Composition root of the app (`03-implementation/05` §1).
///
/// Providers are created once here and are not rebuilt from the widget tree, so a settings change or a token
/// refresh cannot recreate every screen and lose scroll position.
///
/// Two modes live side by side while the backend's M2 endpoints do not exist yet:
///  * **prototype** (the default) renders the screens against generated fake data, with every threshold, dwell and
///    suppression decision made by the real rule kernel in `lib/prototype/rules/`;
///  * **live** (`--dart-define=PROTOTYPE=false`) is the M1 path: the real `ApiClient` and `TelemetryProvider`, which
///    show honest empty and offline states until the API has something to say.
///
/// Both are built and only one is shown, so the prototype cannot quietly rot away from the data layer it imitates.
void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final clock = Clock.system();
  final api = ApiClient(baseUrl: Env.apiBase);
  final settings = SettingsProvider();

  runApp(
    MultiProvider(
      providers: [
        Provider<Clock>.value(value: clock),
        Provider<ApiClient>.value(value: api),
        ChangeNotifierProvider<SettingsProvider>.value(value: settings),
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
}
