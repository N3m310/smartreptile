using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Signals;
using SmartReptile.Domain.Summaries;

namespace SmartReptile.Application.Signals;

/// <summary>
/// The <c>LightDeficit</c> sweep (FR-11/FR-14, roadmap task 3.6): once a terrarium's local day has closed, judges
/// whether it accumulated the light its profile requires and raises one signal when it did not.
/// </summary>
/// <remarks>
/// A sweep rather than a per-sample rule, because the condition is an <b>accumulation</b>: no single reading is
/// wrong, and the number that matters only exists once the day is over. It reads the stored readings rather than a
/// running counter for the same reason the summary recomputer does — a restart, a back-fill and a retry then all take
/// the same path, so the alert and the stored <c>DailyEnvironmentalSummary.LightDeficitHours</c> describe the same
/// arithmetic.
/// <para>
/// Two things it deliberately does not do. It raises no per-sample light alert: a single dark minute at noon is not a
/// failure, and the per-metric band already judges the instantaneous value. And it never judges a day twice — the
/// alert carries the instant the day closed, so the rollover at local midnight is a comparison of dates rather than a
/// second scan of history.
/// </para>
/// </remarks>
public sealed class LightDeficitMonitor(ILightDeficitStore store, IClock clock)
{
    /// <summary>Judges every eligible terrarium once.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<LightDeficitOutcome> SweepAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await store.FindCandidatesAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;

        var judged = 0;
        var opened = 0;
        var resolved = 0;
        var touched = 0;
        var changes = new List<AlertChange>();

        foreach (var candidate in candidates)
        {
            if (!TryGetZone(candidate.TimeZoneId, out var zone))
            {
                // A zone the host cannot resolve is a deployment fault, not a terrarium's: skipping it keeps the rest
                // of the fleet judged instead of failing the whole sweep on one bad id.
                continue;
            }

            var localNow = TimeZoneInfo.ConvertTime(now, zone);
            var localDate = DateOnly.FromDateTime(localNow.DateTime);
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);
            var dayStartUtc = LocalBoundaryToUtc(localDate, TimeOnly.MinValue, zone);
            var open = await store.FindOpenAlertAsync(candidate.TerrariumId, cancellationToken)
                .ConfigureAwait(false);

            // The day rolled over: yesterday's deficit is history. Closing it here is what makes tomorrow's alert a
            // new row describing a new day rather than one row living forever.
            if (open is not null && LocalDateOf(open.TriggeredAt, zone) != localDate)
            {
                HabitatSignalAlertWriter.ResolveLightDeficit(open, dayStartUtc);
                changes.Add(new AlertChange(open, AlertEvent.Resolved));
                resolved++;
                open = null;
            }

            if (!LightDeficitPolicy.IsDue(localTime))
            {
                continue;
            }

            judged++;

            // The light part of the day, not the whole day: judging [00:00, 21:00) is what the rule asks, and it keeps
            // the result stable however late in the evening the sweep happens to run.
            var dueAtUtc = LocalBoundaryToUtc(localDate, LightDeficitPolicy.EvaluationLocalTime, zone);
            var readings = await store
                .FindLightReadingsAsync(candidate.TerrariumId, dayStartUtc, dueAtUtc, cancellationToken)
                .ConfigureAwait(false);

            var lightHours = MetricGridSummarizer
                .Summarize(
                    MetricCode.LightLux,
                    readings.Select(reading => (reading.AtUtc, reading.Value)),
                    dayStartUtc,
                    dueAtUtc,

                    // No band: this rule compares an accumulation against the profile's requirement, not a value
                    // against a target.
                    _ => null,
                    candidate.LightThresholdLux)
                .LightHours;
            var deficit = LightDeficitPolicy.DeficitHours(candidate.MinLightHoursPerDay, lightHours);

            // Rounded to the scale the summary column stores (`decimal(8,3)`), so the alert and
            // `DailyEnvironmentalSummary.LightDeficitHours` can never report different numbers for one day. The tail
            // is an artefact of summing 1/60 h per minute, not a measurement.
            deficit = Math.Round(deficit, 3, MidpointRounding.AwayFromZero);

            if (deficit > 0m)
            {
                if (open is null)
                {
                    var raised = HabitatSignalAlertWriter.OpenLightDeficit(
                        candidate.TerrariumId,
                        candidate.DeviceId,
                        dueAtUtc,
                        deficit);

                    store.AddAlert(raised);
                    changes.Add(new AlertChange(raised, AlertEvent.Opened));
                    opened++;
                }
                else
                {
                    // Already reported for this day: the row moves and no second one is written. A touch is not a
                    // lifecycle move, so it is not announced — the same rule the clock-skew entry follows.
                    HabitatSignalAlertWriter.Touch(open, now, deficit);
                    touched++;
                }

                continue;
            }

            if (open is not null)
            {
                // The day met its requirement after all — a back-filled reading can do this — so the signal closes.
                HabitatSignalAlertWriter.ResolveLightDeficit(open, now);
                changes.Add(new AlertChange(open, AlertEvent.Resolved));
                resolved++;
            }
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LightDeficitOutcome(judged, opened, resolved, touched)
        {
            AlertChanges = changes,
        };
    }

    /// <summary>
    /// The instant a local date and time start in UTC, resolving the two cases a zone can pose.
    /// </summary>
    /// <remarks>
    /// A time the zone skips (a spring-forward gap) is moved forward to the first instant that exists, and an
    /// ambiguous one (a fall-back hour) takes the earlier offset — the same pair of rules the summary recomputer
    /// applies to a local-day boundary, so the day this sweep judges is the day the summary stores.
    /// </remarks>
    /// <param name="date">Local date.</param>
    /// <param name="time">Local time of day.</param>
    /// <param name="zone">Zone to interpret them in.</param>
    private static DateTimeOffset LocalBoundaryToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The local date an instant fell on, which is how a day's episode is recognised.</summary>
    /// <param name="instantUtc">Instant to interpret.</param>
    /// <param name="zone">Zone to interpret it in.</param>
    private static DateOnly LocalDateOf(DateTimeOffset instantUtc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instantUtc, zone).DateTime);

    /// <summary>Resolves a zone id, returning false for one this host cannot load.</summary>
    /// <param name="timeZoneId">IANA or Windows id.</param>
    /// <param name="zone">The resolved zone.</param>
    private static bool TryGetZone(string timeZoneId, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
