import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:smart_reptile/main.dart' as app;
import 'package:smart_reptile/screens/dashboard_screen.dart';
import 'package:smart_reptile/screens/login_screen.dart';

import 'helpers/live_api.dart';
import 'helpers/pump.dart';
import 'helpers/session.dart';

/// Suite `fullstack` / alerts â€” `docs/04-quality/05-e2e-ai-testing-plan.md` Â§5 P4 (`E2E-AI-14`).
///
/// `TC-E2E-03` is the highest-value case in the project (`FR-11`), and the only part of it that needs the ESP32 is
/// *causing* the excursion. A telemetry batch over the HTTPS fallback (`POST /api/v1/ingest/http`, the device-facing
/// route) runs through the **same** `IngestPipeline` as MQTT â€” validator, persistence, evaluator â€” so the alert this
/// case reads in the app is the alert a real board would raise, with `IngestSource.HttpFallback` the only difference.
///
/// Two things make it finish in a test rather than in five minutes: a band written by the fixture with a one-minute
/// dwell (the API's minimum), and a batch stamped in the past and spread across the dwell, so the evaluator sees a
/// window that has already elapsed instead of waiting for one.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  final api = LiveApi();
  final stamp = DateTime.now().millisecondsSinceEpoch;
  final username = 'e2e.alert.$stamp';
  const password = 'E2e!Reptile1';

  late LiveSession session;
  late String terrariumId;
  late String terrariumName;
  late String deviceSecret;
  late String deviceId;

  setUpAll(() async {
    session = await api.registerAndSignIn(username, password);
    final speciesProfileId = await api.firstSpeciesProfileId(
      session.accessToken,
    );
    terrariumName = 'E2E alert enclosure $stamp';
    terrariumId = await api.createTerrarium(
      session.accessToken,
      name: terrariumName,
      speciesProfileId: speciesProfileId,
    );

    // The band this case judges against, and the reason it is not the seeded one: 24â€“30 Â°C with a one-minute dwell,
    // so a batch that ran above 30 Â°C for three minutes is a finished excursion rather than an in-progress one.
    await api.setTemperatureOverride(session.accessToken, terrariumId);

    // Letters and digits only: the API refuses anything else in a chip id (`chip_id_invalid`).
    final registered = await api.selfRegisterDevice('e2eAlert$stamp');
    deviceId = registered.deviceId;
    deviceSecret = await api.claimDevice(
      session.accessToken,
      claimCode: registered.claimCode,
      terrariumId: terrariumId,
    );
  });

  tearDownAll(() async {
    // `allowUnboundDevice` because this fixture bound a board on purpose: without it the API answers
    // `409 conflict_device_bound` and the enclosure would outlive the run.
    final status = await api.deleteTerrarium(session.accessToken, terrariumId);
    expect(
      status,
      204,
      reason: 'teardown could not delete the fixture enclosure',
    );
    final remaining = await api.terrariumNames(session.accessToken);
    expect(
      remaining,
      isEmpty,
      reason: 'the run left enclosures behind: $remaining',
    );
    api.close();
  });

  setUp(clearStoredState);

  testWidgets(
    'E2E-AI-14 an excursion ingested from the device reaches the app',
    (tester) async {
      final semantics = tester.ensureSemantics();
      try {
        await app.main();
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('login.identifier'),
        );
        await tester.enterText(
          find.bySemanticsIdentifier('login.identifier'),
          session.username,
        );
        await tester.enterText(
          find.bySemanticsIdentifier('login.password'),
          session.password,
        );
        await tester.tap(find.bySemanticsIdentifier('login.submit'));
        await pumpUntilGone(tester, find.byType(LoginScreen));
        await pumpUntilFound(tester, find.byType(DashboardScreen));

        // Before the excursion: the alerts destination has nothing for this enclosure.
        await tapDestination(tester, Icons.warning_amber_rounded);
        await tester.pump(const Duration(seconds: 2));
        expect(
          find.textContaining(terrariumName),
          findsNothing,
          reason: 'the alerts list already mentioned the enclosure before any sample was ingested',
        );

        // The excursion itself: four samples over three minutes, all above the 30 Â°C target maximum.
        final status = await api.ingestHotBatch(
          deviceId: deviceId,
          secret: deviceSecret,
          seq: stamp,
        );
        expect(status, 202, reason: 'the ingest route refused the batch');

        // The server has the alert; the app has to be told. Pull-to-refresh is the app's own path for that
        // (`RefreshIndicator(onRefresh: data.loadAll)`), so the case uses it instead of a restart.
        await tester.fling(
          find.byType(ListView).first,
          const Offset(0, 400),
          1200,
        );
        await tester.pump(const Duration(milliseconds: 400));
        await pumpUntilFound(tester, find.textContaining(terrariumName));

        expect(find.textContaining(terrariumName), findsWidgets);

        // And the server agrees, which is what makes this an end-to-end assertion rather than a rendering one.
        final alerts = await api.alerts(session.accessToken);
        final forThisEnclosure = alerts.where(
          (alert) => alert['terrariumId'] == terrariumId,
        );
        expect(
          forThisEnclosure,
          isNotEmpty,
          reason: 'no alert row was written for the enclosure',
        );
        expect(
          forThisEnclosure.map((alert) => alert['metric']),
          contains('tempC'),
        );
        expect(tester.takeException(), isNull);
      } finally {
        semantics.dispose();
      }
    },
  );
}
