using FluentAssertions;
using SmartReptile.Application.Signals;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Application.Signals;

/// <summary>
/// The <c>LightDeficit</c> sweep (`TC-U-31`): a completed local day that accumulated less light than the profile
/// requires raises exactly one signal, and no per-sample light alert is ever raised.
/// </summary>
/// <remarks>
/// The fixture is the case the test table writes down — six hours above 1 000 lx against an eight-hour requirement,
/// judged at 21:00 in `Asia/Ho_Chi_Minh` — expressed as real minute readings so the arithmetic goes through the same
/// minute grid the stored daily summary uses. The two dark readings bracketing the lit stretch are deliberate: they
/// stop the grid's three-minute hold-over from touching the count, so six hours is exactly six hours.
/// </remarks>
public class LightDeficitMonitorTests
{
    /// <summary>2026-10-19 00:00 local (UTC+7) as a UTC instant.</summary>
    private static readonly DateTimeOffset LocalMidnight = new(2026, 10, 18, 17, 0, 0, TimeSpan.Zero);

    private static readonly Guid TerrariumId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();

    private readonly FakeLightDeficitStore _store = new();

    public LightDeficitMonitorTests() =>
        _store.Candidates.Add(new LightDeficitCandidate(TerrariumId, DeviceId, "Asia/Ho_Chi_Minh", 1000m, 8m));

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task At_twenty_one_hundred_six_hours_of_light_raises_one_two_hour_deficit()
    {
        AddSixLitHours();
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(21, 0));

        var outcome = await monitor.SweepAsync();

        outcome.Opened.Should().Be(1);
        outcome.Resolved.Should().Be(0);

        var alert = _store.Added.Should().ContainSingle().Subject;
        alert.Source.Should().Be(AlertSource.LightDeficit);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.Metric.Should().Be(MetricCode.LightLux);
        alert.Family.Should().Be(AlertFamily.Enclosure);
        alert.State.Should().Be(AlertState.Open);
        alert.TriggeringValue.Should().Be(2.0m, "eight required hours minus six accumulated is two");
        alert.TriggeredAt.Should().Be(
            LocalMidnight.AddHours(21),
            "the row is dated from the instant the day closed, not from the sweep that noticed");
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task No_per_sample_light_alert_is_raised_while_the_day_is_still_being_measured()
    {
        AddSixLitHours();
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(20, 59));

        var outcome = await monitor.SweepAsync();

        // One dark minute at noon is not a failure and a day in progress is not judged: the band engine judges the
        // instantaneous value, and this rule exists for the accumulation only.
        outcome.Opened.Should().Be(0);
        outcome.Terrariums.Should().Be(0);
        _store.Added.Should().BeEmpty();
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task A_second_sweep_touches_the_open_row_instead_of_opening_another()
    {
        AddSixLitHours();
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(21, 0));

        await monitor.SweepAsync();
        var second = await monitor.SweepAsync();

        second.Opened.Should().Be(0);
        second.Touched.Should().Be(1);
        _store.Added.Should().ContainSingle("one entry is what a keeper reads as 'still like this'");
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task A_day_that_met_its_requirement_opens_nothing()
    {
        AddLitHours(8);
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(21, 0));

        var outcome = await monitor.SweepAsync();

        outcome.Opened.Should().Be(0);
        _store.Added.Should().BeEmpty();
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task A_back_filled_requirement_closes_an_open_deficit()
    {
        AddLitHours(6, startingAtHour: 7);
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(21, 0));

        await monitor.SweepAsync();

        // Evidence that arrives later changes the day: the sweep recomputes from stored readings, so the signal it
        // raised is the signal it can withdraw.
        AddLitHours(3, startingAtHour: 13);
        var second = await monitor.SweepAsync();

        second.Resolved.Should().Be(1);
        _store.Added.Should().ContainSingle().Which.State.Should().Be(AlertState.Resolved);
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task The_next_local_day_closes_the_previous_days_deficit()
    {
        AddSixLitHours();
        await new LightDeficitMonitor(_store, ClockAtLocal(21, 0)).SweepAsync();

        // 09:00 the next day: today's light is not judgeable yet, and yesterday's entry is history.
        var nextDay = new TestClock { UtcNow = LocalMidnight.AddHours(24 + 9) };
        var outcome = await new LightDeficitMonitor(_store, nextDay).SweepAsync();

        outcome.Resolved.Should().Be(1);
        outcome.Opened.Should().Be(0);
        _store.Added.Should().ContainSingle().Which.State.Should().Be(AlertState.Resolved);
        _store.Added.Single().ResolvedReason.Should().Be(ResolvedReason.Recovered);
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public async Task A_terrarium_whose_zone_cannot_be_resolved_is_skipped_rather_than_failing_the_sweep()
    {
        _store.Candidates.Add(new LightDeficitCandidate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Not/AZone",
            1000m,
            8m));
        AddSixLitHours();
        var monitor = new LightDeficitMonitor(_store, ClockAtLocal(21, 0));

        var outcome = await monitor.SweepAsync();

        outcome.Opened.Should().Be(1, "the healthy terrarium is still judged");
    }

    /// <summary>06:59 dark, 07:00–12:59 lit, 13:00 dark: exactly six hours above 1 000 lx.</summary>
    private void AddSixLitHours() => AddLitHours(6);

    /// <summary>Adds <paramref name="hours"/> of lit minutes bracketed by two dark readings.</summary>
    /// <param name="hours">Lit hours to add.</param>
    /// <param name="startingAtHour">Local hour the lit stretch starts at.</param>
    private void AddLitHours(int hours, int startingAtHour = 7)
    {
        var start = LocalMidnight.AddHours(startingAtHour);

        // One dark reading before the stretch and one after it, so the minute grid holds the lit value only for the
        // minutes that actually carried it.
        _store.Readings.Add(new LightReadingPoint(start.AddMinutes(-1), 0m));

        for (var minute = 0; minute < hours * 60; minute++)
        {
            _store.Readings.Add(new LightReadingPoint(start.AddMinutes(minute), 1200m));
        }

        _store.Readings.Add(new LightReadingPoint(start.AddMinutes(hours * 60), 0m));
    }

    private static TestClock ClockAtLocal(int hour, int minute) =>
        new() { UtcNow = LocalMidnight.AddHours(hour).AddMinutes(minute) };

    /// <summary>In-memory <see cref="ILightDeficitStore"/> that behaves like the database where the sweep can tell.</summary>
    private sealed class FakeLightDeficitStore : ILightDeficitStore
    {
        public List<LightDeficitCandidate> Candidates { get; } = [];

        public List<LightReadingPoint> Readings { get; } = [];

        public List<Alert> Added { get; } = [];

        public Alert? Open { get; private set; }

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<LightDeficitCandidate>> FindCandidatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LightDeficitCandidate>>(Candidates);

        public Task<Alert?> FindOpenAlertAsync(Guid terrariumId, CancellationToken cancellationToken) =>
            Task.FromResult(Open);

        public Task<IReadOnlyList<LightReadingPoint>> FindLightReadingsAsync(
            Guid terrariumId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LightReadingPoint>>(
                [.. Readings.Where(reading => reading.AtUtc >= fromUtc && reading.AtUtc < toUtc)]);

        public void AddAlert(Alert alert)
        {
            Added.Add(alert);

            // What the insert does: the next sweep finds the row it just opened.
            Open = alert;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;

            if (Open?.State == AlertState.Resolved)
            {
                Open = null;
            }

            return Task.CompletedTask;
        }
    }
}
