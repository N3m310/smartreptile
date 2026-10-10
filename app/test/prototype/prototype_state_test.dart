import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/clock.dart';
import 'package:smart_reptile/core/status.dart';
import 'package:smart_reptile/prototype/fake_world.dart';
import 'package:smart_reptile/prototype/prototype_state.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/silence.dart';
import 'package:smart_reptile/prototype/rules/species_profiles.dart';

/// The store is where the prototype's *rules* meet its *mutations*, so these tests are about transitions rather than
/// about pixels: which actions are allowed, which are refused, and what the screens will therefore be able to show.
void main() {
  final now = DateTime(2026, 9, 30, 20, 41);
  late PrototypeState state;

  setUp(() {
    state = PrototypeState.forWorld(
      Clock.fake(now),
      FakeWorldFactory.build(now: now),
    );
  });

  PrototypeState signedIn() {
    state.signIn(
      email: PrototypeState.demoEmail,
      password: PrototypeState.demoPassword,
    );
    return state;
  }

  group('session', () {
    test('starts signed out, so the splash screen decides where to go', () {
      expect(state.isSignedOut, isTrue);
      expect(state.user, isNull);
    });

    test('the demo credentials sign in and the wrong ones do not', () {
      expect(
        state.signIn(email: 'linh@example.com', password: 'wrong'),
        isNotEmpty,
      );
      expect(state.isSignedOut, isTrue);

      expect(
        state.signIn(
          email: PrototypeState.demoEmail,
          password: PrototypeState.demoPassword,
        ),
        isEmpty,
      );
      expect(state.isSignedIn, isTrue);
      expect(state.user?.role, UserRole.owner);
    });

    test('an unknown account and a wrong password give the same message', () {
      final unknown = state.signIn(
        email: 'nobody@example.com',
        password: 'demo1234',
      );
      final wrong = state.signIn(email: 'linh@example.com', password: 'nope');

      expect(
        unknown,
        wrong,
        reason: 'the message must not reveal which emails exist',
      );
    });

    test('signing out clears the inbox filter with the session', () {
      signedIn();
      state.setAlertFilter(const AlertFilter(severity: Severity.critical));
      expect(state.alertFilter.isEmpty, isFalse);

      state.signOut();

      expect(state.isSignedOut, isTrue);
      expect(state.alertFilter.isEmpty, isTrue);
    });

    test('registration validates before it accepts anything', () {
      expect(
        state.register(name: 'L', email: 'a@b.c', password: 'Longenough1!'),
        isNotEmpty,
      );
      expect(
        state.register(name: 'Linh', email: 'nope', password: 'Longenough1!'),
        isNotEmpty,
      );
      expect(
        state.register(name: 'Linh', email: 'a@b.c', password: 'Short1!'),
        isNotEmpty,
        reason: 'the password is shorter than the policy allows (BR-01.2)',
      );
      expect(
        state.register(name: 'Linh', email: 'a@b.c', password: 'longenough1!'),
        isNotEmpty,
        reason: 'no upper-case letter',
      );
      expect(
        state.register(name: 'Linh', email: 'a@b.c', password: 'Longenough12'),
        isNotEmpty,
        reason: 'no special character',
      );
      expect(
        state.register(name: 'Linh', email: 'a@b.c', password: 'Longenough1!'),
        isEmpty,
      );
      expect(state.isSignedIn, isTrue);
    });
  });

  group('BR-12.1 alert lifecycle is a one-way door', () {
    test(
      'acknowledge records who and when, and refuses a second acknowledgement',
      () {
        final open = signedIn().openAlertsFor('t-gecko').single;

        expect(state.acknowledge(open.id), isTrue);
        expect(open.state, AlertState.acknowledged);
        expect(open.acknowledgedBy, 'Linh Trần');
        expect(open.acknowledgedAt, state.demoNow);
        expect(open.isOpen, isTrue, reason: 'acknowledged is not resolved');
        expect(state.acknowledge(open.id), isFalse);
      },
    );

    test(
      'resolve records the reason, and history is never mutated afterwards',
      () {
        final open = signedIn().openAlertsFor('t-gecko').single;

        expect(
          state.resolve(
            open.id,
            ResolvedReason.sensorFault,
            note: 'Probe loose',
          ),
          isTrue,
        );
        expect(open.state, AlertState.resolved);
        expect(open.resolvedReason, ResolvedReason.sensorFault);
        expect(open.note, 'Probe loose');
        expect(open.isOpen, isFalse);
        // BR-12.1: Resolved is terminal. A second transition must be refused rather than quietly rewrite the record.
        expect(state.resolve(open.id, ResolvedReason.recovered), isFalse);
        expect(open.resolvedReason, ResolvedReason.sensorFault);
      },
    );

    test('the open-alert badge count follows the transitions', () {
      signedIn();
      final before = state.openAlertCount;
      final open = state.openAlertsFor('t-gecko').single;

      state.acknowledge(open.id);
      expect(
        state.openAlertCount,
        before,
        reason: 'acknowledged still needs attention',
      );

      state.resolve(open.id, ResolvedReason.recovered);
      expect(state.openAlertCount, before - 1);
    });

    test('false positive and sensor fault are kept as v2 training labels', () {
      expect(ResolvedReason.falsePositive.isLabelForV2Training, isTrue);
      expect(ResolvedReason.sensorFault.isLabelForV2Training, isTrue);
      expect(ResolvedReason.recovered.isLabelForV2Training, isFalse);
    });
  });

  group('BR-12.4 the inbox filter', () {
    test('filters by severity, state and metric together', () {
      signedIn();
      final critical = state.allAlerts
          .where((a) => a.severity == Severity.critical)
          .toList();

      state.setAlertFilter(const AlertFilter(severity: Severity.critical));
      expect(state.filteredAlerts.length, critical.length);

      state.setAlertFilter(
        const AlertFilter(severity: Severity.critical, metric: 'humidityPct'),
      );
      expect(
        state.filteredAlerts.every((a) => a.metric == 'humidityPct'),
        isTrue,
      );
    });

    test('an empty filter returns everything', () {
      signedIn();
      expect(state.filteredAlerts.length, state.allAlerts.length);
    });

    test('filtering by terrarium hides the rest, which is how ownership stays invisible', () {
      signedIn();
      state.setAlertFilter(const AlertFilter(terrariumId: 't-quarantine'));

      expect(state.filteredAlerts, isNotEmpty);
      expect(
        state.filteredAlerts.every((a) => a.terrariumId == 't-quarantine'),
        isTrue,
      );
    });
  });

  group('BR-08.1 the live view decides card status from the resolved band', () {
    test('the gecko box shows an open Critical temperature alert and a matching card', () {
      signedIn();
      final view = state.liveView('t-gecko');

      expect(view.hasNoData, isFalse);
      expect(view.bannerAlert, isNotNull);
      expect(view.bannerAlert!.severity, Severity.critical);
      expect(
        view.metrics['tempC']!.displayStatus,
        MetricStatus.critical,
        reason: 'the card and the alert must agree about the same reading',
      );
    });

    test('the silent tub is stale and says so instead of looking healthy', () {
      signedIn();
      final view = state.liveView('t-quarantine');

      expect(view.isDeviceOffline, isTrue);
      expect(view.isStale, isTrue);
      expect(view.newestAge!.inMinutes, 42);
    });

    test('the unclaimed rack is the onboarding empty state, not a fault', () {
      signedIn();
      final view = state.liveView('t-beardie');

      expect(view.hasNoData, isTrue);
      expect(view.device, isNull);
      expect(view.openAlerts, isEmpty);
      // The bands are still known, because the profile is assigned — so the thresholds screen has something to show
      // even before a node exists.
      expect(view.bands['tempC']?.band, isNotNull);
    });

    test('an unmeasured metric is reported as such rather than invented', () {
      signedIn();
      const tropical = ThresholdResolver(profile: null);
      expect(tropical.resolve('uvIndex', now).hasBand, isFalse);

      // The tropical profile has no surface probe, so the dashboard must not show a surface card at all.
      expect(
        state.measuredMetrics('t-quarantine'),
        isNot(contains('surfaceTempC')),
      );
      expect(state.measuredMetrics('t-gecko'), contains('surfaceTempC'));
    });

    test('maintenance mode changes every card to Maintenance', () {
      signedIn();
      state.setMaintenance('sr-3f9a2c', true);

      final view = state.liveView('t-gecko');
      expect(
        view.metrics.values.every(
          (value) => value.displayStatus == MetricStatus.maintenance,
        ),
        isTrue,
      );
      // The device is also no longer reporting on schedule, so the header must not claim it is online.
      expect(view.isDeviceOffline, isTrue);
    });
  });

  group(
    'BR-10.3 thresholds: an override must reach the engine, not just the label',
    () {
      const tighter = ThresholdBand(
        metric: 'tempC',
        phase: Phase.day,
        targetMin: 26,
        targetMax: 30,
        criticalMin: 22,
        criticalMax: 33,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 0.5,
      );

      test('a valid override is applied and labelled as an override', () {
        signedIn();
        // A day-phase override genuinely does not apply at 20:41, when the night band is in force, so the demo clock is
        // moved into the photoperiod. Asserting on the day band while the app is in the night phase is how the first
        // version of these tests "proved" that overrides were broken.
        state.setDemoClock(DateTime(now.year, now.month, now.day, 14, 20));

        expect(
          state.applyOverride('t-gecko', tighter, reason: 'Cooler species'),
          isTrue,
        );
        final effective = state
            .bandsFor('t-gecko')
            .firstWhere((band) => band.metric == 'tempC');

        expect(effective.source, BandSource.override);
        expect(effective.band?.targetMax, 30);
        expect(state.overridesFor('t-gecko'), hasLength(1));
        expect(state.overridesFor('t-gecko').single.reason, 'Cooler species');
        expect(state.overridesFor('t-gecko').single.editedBy, 'Linh Trần');
      });

      test(
        'an override with an inverted band is refused and changes nothing',
        () {
          signedIn();

          expect(
            state.applyOverride(
              't-gecko',
              tighter.copyWith(targetMin: 40, targetMax: 30),
            ),
            isFalse,
          );
          expect(state.overridesFor('t-gecko'), isEmpty);
          expect(
            state
                .bandsFor('t-gecko')
                .firstWhere((b) => b.metric == 'tempC')
                .source,
            BandSource.profile,
          );
        },
      );

      test('clearing the override restores the profile band', () {
        signedIn();
        state.setDemoClock(DateTime(now.year, now.month, now.day, 14, 20));
        state.applyOverride('t-gecko', tighter);
        state.clearOverride('t-gecko', 'tempC', Phase.day);

        final effective = state
            .bandsFor('t-gecko')
            .firstWhere((band) => band.metric == 'tempC');
        expect(effective.source, BandSource.profile);
        expect(effective.band?.targetMax, 32);
      });

      test(
        'a day override is correctly ignored at night — the phase rule working',
        () {
          signedIn();
          state.setDemoClock(DateTime(now.year, now.month, now.day, 14, 20));
          state.applyOverride('t-gecko', tighter);

          // 20:41 is outside the photoperiod, so the night band applies and the daytime override is not in force. The
          // override is still on record; it simply is not the band in force right now (BR-11.2).
          state.setDemoClock(now);
          final effective = state
              .bandsFor('t-gecko')
              .firstWhere((band) => band.metric == 'tempC');

          expect(effective.band?.phase, Phase.night);
          expect(effective.band?.targetMax, 27);
          expect(effective.source, BandSource.profile);
          expect(state.overridesFor('t-gecko'), hasLength(1));
        },
      );

      test(
        'validation surfaces the climate warning but does not block the save',
        () {
          signedIn();
          final issues = state.validateBand(
            't-gecko',
            tighter.copyWith(
              targetMin: 10,
              targetMax: 14,
              criticalMin: 5,
              criticalMax: 20,
            ),
          );

          expect(issues, isNotEmpty);
          expect(issues.every((issue) => !issue.isBlocking), isTrue);
          expect(
            state.applyOverride(
              't-gecko',
              tighter.copyWith(
                targetMin: 10,
                targetMax: 14,
                criticalMin: 5,
                criticalMax: 20,
              ),
            ),
            isTrue,
            reason: 'BR-10.5 is a warning by design, never a refusal',
          );
        },
      );
    },
  );

  group('FR-04 claiming a device', () {
    test(
      'the code must be 8 characters once the display dashes are removed',
      () {
        signedIn();

        expect(
          state.claimDevice(code: 'K7M2-Q', terrariumId: 't-beardie'),
          isNotEmpty,
        );
        expect(
          state.deviceAwaitingClaim,
          isNotNull,
          reason: 'nothing was claimed',
        );
      },
    );

    test('an unknown code is refused', () {
      signedIn();

      expect(
        state.claimDevice(code: 'ZZZZZZZZ', terrariumId: 't-beardie'),
        isNotEmpty,
      );
      expect(state.deviceFor('t-beardie'), isNull);
    });

    test('the real code claims the node, which goes online and stops showing a code', () {
      signedIn();
      final code = state.deviceAwaitingClaim!.claimCode!;

      expect(
        state.claimDevice(
          code: '${code.substring(0, 4)}-${code.substring(4)}',
          terrariumId: 't-beardie',
        ),
        isEmpty,
      );

      final claimed = state.deviceFor('t-beardie')!;
      expect(claimed.id, 'sr-9c44d1');
      expect(claimed.status, DeviceStatus.online);
      expect(claimed.lastSeenAt, state.demoNow);
      expect(
        state.deviceAwaitingClaim,
        isNull,
        reason: 'a claimed node must stop advertising a pairing code',
      );
      expect(state.liveView('t-beardie').device, isNotNull);
    });

    test('a terrarium that already has a node cannot take a second one', () {
      signedIn();

      expect(
        state.claimDevice(code: 'K7M2QP4T', terrariumId: 't-gecko'),
        isNotEmpty,
      );
    });

    test('an expired code is refused, because the node rotates it every 15 minutes', () {
      signedIn();
      // Move the demo clock past the code's expiry rather than waiting for it.
      state.advanceDemoClock(const Duration(minutes: 12));

      expect(
        state.claimDevice(code: 'K7M2QP4T', terrariumId: 't-beardie'),
        contains('expired'),
      );
    });
  });

  group('BR-12.6 silence windows', () {
    test('a silence needs a reason', () {
      signedIn();

      expect(
        state.createSilence(
          terrariumId: 't-gecko',
          metric: 'tempC',
          duration: const Duration(hours: 1),
          reason: '   ',
        ),
        isFalse,
      );
      expect(state.activeSilences, isEmpty);
    });

    test('a silence is clamped to 24 hours and attributed to the actor', () {
      signedIn();
      state.createSilence(
        terrariumId: 't-gecko',
        metric: 'tempC',
        duration: const Duration(hours: 48),
        reason: 'Lamp replacement',
      );

      final silence = state.activeSilences.single;
      expect(silence.until.difference(silence.from), SilenceWindow.maxDuration);
      expect(silence.createdBy, 'Linh Trần');
      expect(silence.terrariumId, 't-gecko');
    });

    test('the dashboard is told about it, because a silence must always be visible', () {
      signedIn();
      state.createSilence(
        terrariumId: 't-gecko',
        metric: 'tempC',
        duration: const Duration(hours: 2),
        reason: 'Lamp replacement',
      );

      expect(state.liveView('t-gecko').silences, hasLength(1));
      expect(state.liveView('t-quarantine').silences, isEmpty);
    });

    test('an expired silence disappears on its own', () {
      signedIn();
      state.createSilence(
        terrariumId: 't-gecko',
        metric: 'tempC',
        duration: const Duration(hours: 1),
        reason: 'Lamp replacement',
      );

      state.advanceDemoClock(const Duration(hours: 2));

      expect(state.activeSilences, isEmpty);
      expect(state.liveView('t-gecko').silences, isEmpty);
    });
  });

  group('BR-13.2 notification preferences', () {
    test('preferences start from the user and can be changed', () {
      signedIn();
      expect(state.preferences.minSeverity, Severity.warning);

      state.setPreferences(
        state.preferences.copyWith(
          minSeverity: Severity.critical,
          quietHoursEnabled: false,
        ),
      );

      expect(state.preferences.minSeverity, Severity.critical);
      expect(state.preferences.quietHoursEnabled, isFalse);
      expect(
        state.preferences.isQuietHour(23),
        isFalse,
        reason: 'switching quiet hours off must actually switch them off',
      );
    });
  });

  group('FR-16 device management', () {
    test('rebinding moves a node, and unbinding returns it to the fleet', () {
      signedIn();
      // The empty rack is the only terrarium with no node of its own, so it is the only honest destination here.
      state.rebindDevice('sr-3f9a2c', 't-beardie');
      expect(state.deviceFor('t-beardie')!.id, 'sr-3f9a2c');
      expect(state.deviceFor('t-gecko'), isNull);

      state.rebindDevice('sr-3f9a2c', null);
      expect(state.deviceFor('t-beardie'), isNull);
      expect(state.deviceById('sr-3f9a2c')!.isClaimed, isFalse);
    });

    test('revoking a device is recorded as its own state', () {
      signedIn();

      state.revokeDevice('sr-71b0e5');

      expect(state.deviceById('sr-71b0e5')!.status, DeviceStatus.revoked);
    });

    test('calibration offsets are stored on the device', () {
      signedIn();

      state.setCalibration(
        'sr-3f9a2c',
        tempOffsetC: -0.8,
        rhOffsetPct: 2.5,
        luxGain: 1.04,
      );

      final device = state.deviceById('sr-3f9a2c')!;
      expect(device.tempOffsetC, -0.8);
      expect(device.rhOffsetPct, 2.5);
      expect(device.luxGain, 1.04);
    });
  });

  group('the demo clock is the one thing that makes staleness visible', () {
    test('advancing it makes an otherwise fresh terrarium stale', () {
      signedIn();
      final before = state.liveView('t-gecko');
      expect(before.isStale, isFalse);

      state.advanceDemoClock(const Duration(minutes: 10));

      final after = state.liveView('t-gecko');
      expect(after.isStale, isTrue);
      expect(after.newestAge!.inMinutes, 10);
      // The values are still there — a stale reading is dimmed, never deleted (BR-08.5).
      expect(after.metrics, isNotEmpty);
    });

    test('resetting returns to the instant the data ends', () {
      signedIn();
      state.advanceDemoClock(const Duration(hours: 3));
      state.resetDemoClock();

      expect(state.demoNow, state.anchor);
      expect(state.liveView('t-gecko').isStale, isFalse);
    });
  });

  group('provenance answers "why is my limit 32?"', () {
    test('without an override it points at the profile', () {
      signedIn();
      final provenance = state.provenanceFor('t-gecko', 'tempC');

      expect(provenance.source, BandSource.profile);
      expect(provenance.profileId, 'semi-arid-leopard-gecko');
      expect(provenance.hasOverride, isFalse);
    });

    test('with one it points at the override', () {
      signedIn();
      final band = state
          .bandsFor('t-gecko')
          .firstWhere((b) => b.metric == 'tempC')
          .band!;
      state.applyOverride('t-gecko', band.copyWith(targetMax: 31));

      final provenance = state.provenanceFor('t-gecko', 'tempC');
      expect(provenance.source, BandSource.override);
      expect(provenance.hasOverride, isTrue);
      expect(provenance.band?.targetMax, 31);
    });
  });

  group('history and summaries', () {
    test('a series is filtered to the window and the metric', () {
      signedIn();
      final from = state.anchor.subtract(const Duration(hours: 1));
      final series = state.seriesFor(
        't-gecko',
        'tempC',
        from: from,
        to: state.anchor,
      );

      expect(series, isNotEmpty);
      expect(series.length, lessThanOrEqualTo(61));
      expect(series.every((s) => s.metric == 'tempC'), isTrue);
      expect(series.every((s) => !s.recordedAt.isBefore(from)), isTrue);
    });

    test('the offline tub has no samples after its last heartbeat', () {
      signedIn();
      final samples = state.historyFor('t-quarantine');
      final newest = samples.last.recordedAt;

      expect(
        state.anchor.difference(newest).inMinutes,
        42,
        reason: 'an offline node must not appear to have kept reporting',
      );
    });

    test('coverage is reported per terrarium, and an unclaimed one reports nothing', () {
      signedIn();

      expect(state.coverage('t-gecko').coveragePct, closeTo(100, 0.01));
      expect(state.coverage('t-gecko').isBelowTarget, isFalse);
      expect(state.coverage('t-beardie').expectedSamples, 0);
      expect(state.coverage('t-beardie').coveragePct, 0);
    });

    test(
      'summaries carry coverage and the light deficit, and never exceed 100 %',
      () {
        signedIn();
        final summaries = state.summariesFor('t-quarantine');

        expect(summaries, hasLength(3));
        expect(summaries.first.lightDeficitHours, greaterThan(5));
        for (final summary in summaries) {
          expect(summary.coveragePct, lessThanOrEqualTo(100));
        }
      },
    );

    test(
      'the notification log can show one incident\'s whole response timeline',
      () {
        signedIn();
        final alert = state.alertsFor('t-gecko').first;

        expect(state.notificationsFor(alert.id), isNotEmpty);
        expect(
          state.notificationsFor(alert.id).every((r) => r.alertId == alert.id),
          isTrue,
        );
      },
    );
  });
}
