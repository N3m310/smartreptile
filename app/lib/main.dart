import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'app.dart';
import 'core/clock.dart';
import 'core/env.dart';
import 'data/api_client.dart';
import 'state/app_data_provider.dart';
import 'state/settings_provider.dart';
import 'state/telemetry_provider.dart';

/// Composition root of the app (`03-implementation/05` §1).
void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final clock = Clock.system();
  final api = ApiClient(baseUrl: Env.apiBase);
  final settings = SettingsProvider();
  final appData = AppDataProvider();

  runApp(
    MultiProvider(
      providers: [
        Provider<Clock>.value(value: clock),
        Provider<ApiClient>.value(value: api),
        ChangeNotifierProvider<SettingsProvider>.value(value: settings),
        ChangeNotifierProvider<AppDataProvider>.value(value: appData),
        ChangeNotifierProvider<TelemetryProvider>(
          create: (_) => TelemetryProvider(api: api, clock: clock),
        ),
      ],
      child: const SmartReptileApp(),
    ),
  );

  // Fire-and-forget
  unawaited(settings.load());
}
