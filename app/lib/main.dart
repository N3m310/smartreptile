import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'app.dart';
import 'core/clock.dart';
import 'core/env.dart';
import 'data/api_client.dart';
import 'state/settings_provider.dart';
import 'state/telemetry_provider.dart';

/// Composition root of the app (`03-implementation/05` §1).
///
/// Providers are created once here and are not rebuilt from the widget tree, so a settings change or a token
/// refresh cannot recreate every screen and lose scroll position.
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
      ],
      child: const SmartReptileApp(),
    ),
  );

  // Fire-and-forget: preferences only affect the first frame's theme, and blocking launch on disk I/O is worse
  // than a one-frame system-theme flash.
  unawaited(settings.load());
}
