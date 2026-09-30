import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/events.dart';
import 'package:smart_reptile/prototype/rules/scenarios.dart';

/// The scenario library is the prototype's contract with the documentation.
///
/// Every scenario is a claim taken from `docs/03-implementation/06` or `docs/01-product/03` and turned into data.
/// These tests are what make the Rule Lab worth looking at: if a scenario stops matching, either the engine broke or
/// the document changed, and both are things the team needs to know before the demo rather than during it.
void main() {
  group('every scenario matches its documented expectation', () {
    for (final scenario in Scenarios.all) {
      test('${scenario.id} — ${scenario.title}', () {
        final check = ScenarioRunner.run(scenario);

        expect(
          check.failures,
          isEmpty,
          reason:
              '${scenario.id} (${scenario.docRef}) disagreed with the documentation:\n'
              '${check.failures.join('\n')}\n'
              'trace:\n${check.result.noteworthy.map((s) => '  ${s.at.toIso8601String()} ${s.ruleId} ${s.explanation}').join('\n')}',
        );
      });
    }
  });

  test('the scenario library covers the rules it claims to cover', () {
    final ruleIds = {
      for (final scenario in Scenarios.all)
        ...ScenarioRunner.run(scenario).result.steps.map((step) => step.ruleId),
    };

    // Each of these is a rule a reviewer will look for in the Lab. If one disappears the Lab has quietly stopped
    // demonstrating something, which is worse than failing loudly.
    for (final expected in const [
      'BR-11.1', // flagged readings are skipped
      'BR-11.3', // dwell (both the "no alert" and the "open" outcome)
      'BR-11.4', // recovery margin
      'BR-11.5', // dedupe / extend
      'BR-11.6', // escalation
      'BR-11.8', // late back-fill recorded, not notified
      'BR-12.6', // silence window and maintenance mode
      'BR-13.2', // quiet hours
      'BR-13.3', // hourly rate limit
      'BR-07.2', // device silence
    ]) {
      expect(
        ruleIds,
        contains(expected),
        reason: 'no scenario exercises $expected',
      );
    }

    // BR-11.2 has no step of its own: the phase is carried by the *band* that was resolved, so the way to prove a
    // scenario exercises it is to read the band, not to look for the rule id in a trace.
    final nightBand = ScenarioRunner.run(Scenarios.nightPhase)
        .result
        .steps
        .first
        .band;
    expect(nightBand?.phase, Phase.night);
    expect(nightBand?.targetMax, 27);
  });

  test('scenario B reproduces the document\'s own timeline when the recovery '
      'mode is switched to the one its worked example implies', () {
    final scenario = Scenarios.realExcursion;

    // The default reading of §4.2 requires the margin, which the document's Example B does not apply.
    final strict = ScenarioRunner.run(scenario);
    expect(strict.resolvedAtMinute, 57);

    // §3 Example B resolves at 14:54, i.e. 54 minutes after the 14:00 start — which only happens if any in-band
    // reading counts. That is the contradiction the Lab surfaces.
    final perExample = ScenarioRunner.run(
      scenario,
      config: const EngineConfig(recoveryMode: RecoveryMode.plainInBand),
    );
    expect(perExample.resolvedAtMinute, 54);
    expect(perExample.matchesExpectation, isFalse);
    expect(
      perExample.failures.single,
      contains('expected resolution at minute 57, got 54'),
    );
  });

  test(
    'a sub-dwell spike is the difference between a usable and a muted app',
    () {
      final check = ScenarioRunner.run(Scenarios.belowDwellSpike);

      expect(check.result.alerts, isEmpty);
      expect(check.result.counters.samplesEvaluated, 6);
      // The engine still tracked the excursion internally — it simply never reached the dwell threshold.
      expect(
        check.result.steps.where(
          (step) => step.kind == DecisionKind.belowDwell,
        ),
        isNotEmpty,
      );
    },
  );

  test('dedupe turns twenty excursions into one alert row', () {
    final check = ScenarioRunner.run(Scenarios.dedupe);

    expect(check.result.alerts, hasLength(1));
    final alert = check.result.alerts.single;
    expect(alert.peakValue, 32.4);
    expect(alert.resolvedAt, isNull);
    // One incident, one row — where a naive implementation would report twenty.
    expect(check.result.counters.warningsOpened, 1);
    expect(
      alert.triggeredAt,
      Scenarios.dedupe.start.add(const Duration(minutes: 1)),
    );
  });
}
