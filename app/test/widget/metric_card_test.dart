import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/status.dart';
import 'package:smart_reptile/l10n/generated/app_localizations.dart';
import 'package:smart_reptile/models/metric_value.dart';
import 'package:smart_reptile/widgets/metric_card.dart';

/// TC-W-04 and TC-W-05 in `docs/04-quality/02-test-cases.md`.
///
/// These two behaviours are the reason the card exists: a value must always be shown with its own timestamp and
/// its band, and a stale value must look stale.
void main() {
  final capturedAt = DateTime.utc(2026, 9, 21, 8, 15);

  Widget wrap(Widget child, {Locale locale = const Locale('en')}) =>
      MaterialApp(
        locale: locale,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: SizedBox(width: 220, child: child)),
      );

  testWidgets('renders value, unit, band, status and its own timestamp', (
    tester,
  ) async {
    await tester.pumpWidget(
      wrap(
        MetricCard(
          metricCode: 'tempC',
          displayName: 'Temperature',
          value: 28.6,
          unit: '°C',
          status: MetricStatus.inRange,
          capturedAt: capturedAt,
          now: capturedAt.add(const Duration(seconds: 12)),
          band: const Band(min: 26, max: 32),
        ),
      ),
    );

    expect(find.text('Temperature'), findsOneWidget);
    expect(find.text('28.6'), findsOneWidget);
    expect(find.text('°C'), findsOneWidget);
    expect(find.text('In range'), findsOneWidget);
    // The band is always visible next to the value.
    expect(find.textContaining('26'), findsOneWidget);
    expect(find.textContaining('32'), findsOneWidget);
    // …and the timestamp is part of the card, never optional.
    expect(find.text('12 s'), findsOneWidget);
  });

  testWidgets('announces name, value, unit and status to screen readers', (
    tester,
  ) async {
    await tester.pumpWidget(
      wrap(
        MetricCard(
          metricCode: 'tempC',
          displayName: 'Temperature',
          value: 28.6,
          unit: '°C',
          status: MetricStatus.inRange,
          capturedAt: capturedAt,
          now: capturedAt,
        ),
      ),
    );

    final semantics = tester.getSemantics(find.byType(MetricCard));
    expect(semantics.label, contains('Temperature 28.6 °C'));
    expect(semantics.label, contains('In range'));
  });

  testWidgets(
    'dims the value and flags staleness past three sampling intervals',
    (tester) async {
      // Sampling interval 60 s → stale after 180 s; 12 minutes is well past that.
      await tester.pumpWidget(
        wrap(
          MetricCard(
            metricCode: 'tempC',
            displayName: 'Temperature',
            value: 28.6,
            unit: '°C',
            status: MetricStatus.inRange,
            capturedAt: capturedAt,
            now: capturedAt.add(const Duration(minutes: 12)),
            samplingIntervalSec: 60,
          ),
        ),
      );

      final opacity = tester.widget<Opacity>(
        find.descendant(
          of: find.byType(MetricCard),
          matching: find.byType(Opacity),
        ),
      );
      expect(opacity.opacity, lessThan(1.0));
      expect(find.text('12 min'), findsOneWidget);
    },
  );

  testWidgets('renders critical status with its label, not colour alone', (
    tester,
  ) async {
    await tester.pumpWidget(
      wrap(
        MetricCard(
          metricCode: 'tempC',
          displayName: 'Temperature',
          value: 35.1,
          unit: '°C',
          status: MetricStatus.critical,
          capturedAt: capturedAt,
          now: capturedAt.add(const Duration(seconds: 30)),
          band: const Band(min: 26, max: 32),
        ),
      ),
    );

    expect(find.text('Critical'), findsOneWidget);
    expect(find.byIcon(Icons.error), findsOneWidget);
  });

  testWidgets('renders Vietnamese labels when the locale is vi', (
    tester,
  ) async {
    await tester.pumpWidget(
      wrap(
        MetricCard(
          metricCode: 'humidityPct',
          displayName: 'Độ ẩm',
          value: 41,
          unit: '%RH',
          status: MetricStatus.outOfRange,
          capturedAt: capturedAt,
          now: capturedAt,
          band: const Band(min: 30, max: 40),
        ),
        locale: const Locale('vi'),
      ),
    );

    expect(find.text('Ngoài ngưỡng'), findsOneWidget);
    expect(find.text('41'), findsOneWidget);
  });
}
