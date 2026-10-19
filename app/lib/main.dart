import 'dart:async';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart'
    show TargetPlatform, defaultTargetPlatform, kIsWeb;
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
import 'services/push_registration_service.dart';

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
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final messaging = Env.prototypeMode
      ? null
      : await _initializeFirebaseMessaging();
  final clock = Clock.system();
  final api = ApiClient(baseUrl: Env.apiBase);
  final settings = SettingsProvider();
  final appData = AppDataProvider();
  final pushRegistration = messaging == null
      ? null
      : PushRegistrationService(
          api,
          () async {
            final result = await messaging.requestPermission(
              alert: true,
              badge: true,
              sound: true,
            );
            return result.authorizationStatus ==
                    AuthorizationStatus.authorized ||
                result.authorizationStatus == AuthorizationStatus.provisional;
          },
          // `register`/`onRegistered` returns a Firebase Installation ID. The backend sends through the
          // FCM HTTP v1 `message.token` field, so keep using the actual FCM registration token API.
          // ignore: deprecated_member_use
          messaging.getToken,
          // ignore: deprecated_member_use
          messaging.onTokenRefresh,
        );
  final session = SessionProvider(api: api, pushRegistration: pushRegistration);
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

Future<FirebaseMessaging?> _initializeFirebaseMessaging() async {
  if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) {
    debugPrint('FCM registration is currently supported only on Android.');
    return null;
  }

  if (!Env.firebaseConfigured) {
    debugPrint(
      'FCM is disabled for this build. Provide FIREBASE_API_KEY, FIREBASE_APP_ID, '
      'FIREBASE_PROJECT_ID and FIREBASE_MESSAGING_SENDER_ID to enable it.',
    );
    return null;
  }

  try {
    await Firebase.initializeApp(
      options: const FirebaseOptions(
        apiKey: Env.firebaseApiKey,
        appId: Env.firebaseAppId,
        messagingSenderId: Env.firebaseMessagingSenderId,
        projectId: Env.firebaseProjectId,
      ),
    );
    return FirebaseMessaging.instance;
  } on FirebaseException catch (error, stackTrace) {
    debugPrint(
      'Firebase initialization failed: ${error.message ?? error.code}\n$stackTrace',
    );
    return null;
  }
}
