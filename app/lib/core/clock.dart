/// Injected time source.
///
/// Everything time-dependent in the app (staleness, "12 s ago", alert durations) reads the clock through this
/// interface so tests can advance time instead of waiting. The same pattern is used on the backend
/// (`IClock`) — and it is what keeps `testWidgets` from hanging on real timers (§04-quality/01 §4).
library;

abstract class Clock {
  /// Current instant in UTC.
  DateTime nowUtc();

  /// Convenience: current instant in the local zone, used only for display.
  DateTime nowLocal() => nowUtc().toLocal();

  /// Production clock.
  static Clock system() => _SystemClock();

  /// Test clock frozen at [instant]; move it with [FakeClock.advance].
  static FakeClock fake(DateTime instant) => FakeClock(instant);
}

class _SystemClock extends Clock {
  @override
  DateTime nowUtc() => DateTime.now().toUtc();
}

/// Mutable clock for tests.
class FakeClock extends Clock {
  FakeClock(this._nowUtc);

  DateTime _nowUtc;

  /// Advances the clock by [duration].
  void advance(Duration duration) => _nowUtc = _nowUtc.add(duration);

  /// Moves the clock to an explicit instant.
  void set(DateTime instant) => _nowUtc = instant.toUtc();

  @override
  DateTime nowUtc() => _nowUtc;
}
