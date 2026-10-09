using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Terrariums;

/// <summary>
/// In-memory <see cref="ITerrariumStore"/>. Same rule as the other fakes in this suite: no database, no clock, and
/// the row builders take explicit arguments so a test says what it means.
/// </summary>
/// <remarks>
/// Two behaviours deliberately mirror the real adapter rather than being simpler than it, because tests rely on
/// them: <see cref="FindOwnedAsync"/> returns null for a foreign owner (never a loaded-then-discarded row), and
/// <see cref="SamplesInRangeAsync"/> returns samples carrying only the requested metric, the way the filtered
/// include does.
/// </remarks>
internal sealed class FakeTerrariumStore : ITerrariumStore
{
    private readonly List<Terrarium> _terrariums = [];
    private readonly List<TelemetrySample> _samples = [];
    private readonly List<Alert> _alerts = [];
    private readonly List<SpeciesProfile> _profiles = [];

    /// <summary>How many times the store was committed — asserted so "nothing was written" is a real assertion.</summary>
    public int SaveCount { get; private set; }

    public IReadOnlyList<Terrarium> Terrariums => _terrariums;

    /// <summary>A species profile the create path will accept.</summary>
    public SpeciesProfile AddProfile(string name = "Test species", TimeOnly? lightsOn = null, decimal photoperiodHours = 12m)
    {
        var profile = new SpeciesProfile
        {
            Name = name,
            LightsOnLocalTime = lightsOn ?? new TimeOnly(7, 0),
            PhotoperiodHours = photoperiodHours,
        };

        _profiles.Add(profile);
        return profile;
    }

    /// <summary>A band on a profile. Bounds are explicit so a test can name the value it expects to be judged.</summary>
    public static void AddBand(
        SpeciesProfile profile,
        MetricCode metric,
        decimal targetMin,
        decimal targetMax,
        ThresholdPhase phase = ThresholdPhase.Any,
        decimal? criticalMin = null,
        decimal? criticalMax = null)
    {
        profile.Thresholds.Add(new Threshold
        {
            SpeciesProfileId = profile.Id,
            Metric = metric,
            Phase = phase,
            TargetMin = targetMin,
            TargetMax = targetMax,
            CriticalMin = criticalMin,
            CriticalMax = criticalMax,
            Enabled = true,
        });
    }

    /// <summary>A per-terrarium override, which wins over the profile band (BR-10.3).</summary>
    public static void AddOverride(
        Terrarium terrarium,
        MetricCode metric,
        decimal targetMin,
        decimal targetMax,
        ThresholdPhase phase = ThresholdPhase.Any,
        bool enabled = true)
    {
        terrarium.ThresholdOverrides.Add(new ThresholdOverride
        {
            TerrariumId = terrarium.Id,
            Metric = metric,
            Phase = phase,
            TargetMin = targetMin,
            TargetMax = targetMax,
            Enabled = enabled,
        });
    }

    /// <summary>A terrarium. The species profile is attached as a navigation, the way EF would materialise it.</summary>
    public Terrarium AddTerrarium(
        Guid ownerUserId,
        string name = "Test box",
        SpeciesProfile? profile = null,
        string timeZoneId = "UTC",
        byte[]? rowVersion = null)
    {
        var terrarium = new Terrarium
        {
            UserId = ownerUserId,
            Name = name,
            SpeciesProfileId = profile?.Id ?? Guid.Empty,
            SpeciesProfile = profile,
            TimeZoneId = timeZoneId,

            // A stored row always carries one — it is the database's own token, and the update path compares it.
            RowVersion = rowVersion ?? [1, 2, 3, 4, 5, 6, 7, 8],
        };

        _terrariums.Add(terrarium);
        return terrarium;
    }

    /// <summary>A device bound to <paramref name="terrarium"/>.</summary>
    public static Device AddDevice(
        Terrarium terrarium,
        DeviceStatus status = DeviceStatus.Online,
        DateTimeOffset? lastSeenAt = null,
        int samplingIntervalSec = 60,
        string firmwareVersion = "0.1.0-test")
    {
        var device = new Device
        {
            PublicId = $"sr-{Guid.NewGuid().ToString("N")[..6]}",
            DeviceName = "Test node",
            ChipId = Guid.NewGuid().ToString("N")[..12],
            MacAddress = "AA:BB:CC:DD:EE:FF",
            FirmwareVersion = firmwareVersion,
            Status = status,
            TerrariumId = terrarium.Id,
            Terrarium = terrarium,
            LastSeenAt = lastSeenAt,
            SamplingIntervalSec = samplingIntervalSec,
        };

        terrarium.Device = device;
        return device;
    }

    /// <summary>A telemetry sample with one reading per supplied metric.</summary>
    public TelemetrySample AddSample(
        Guid terrariumId,
        Guid deviceId,
        DateTimeOffset recordedAt,
        QualityFlags qualityFlags = QualityFlags.None,
        params (MetricCode Code, decimal Value)[] readings)
    {
        var sample = new TelemetrySample
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,
            RecordedAt = recordedAt,
            ReceivedAt = recordedAt,
            Sequence = _samples.Count + 1,
            QualityFlags = qualityFlags,
            FirmwareVersion = "0.1.0-test",
            Source = IngestSource.Mqtt,
            Readings = [.. readings.Select(reading => new MetricReading
            {
                Metric = reading.Code,
                Value = reading.Value,
            })],
        };

        _samples.Add(sample);
        return sample;
    }

    /// <summary>An alert row, so the activity summary has something to count.</summary>
    public void AddAlert(Guid terrariumId, Guid deviceId, AlertState state = AlertState.Open) =>
        _alerts.Add(new Alert
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,
            State = state,
            DedupeKey = $"{terrariumId}:tempC:Warning:Any:{_alerts.Count}",
        });

    public Task<IReadOnlyList<Terrarium>> ListOwnedAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Terrarium>>(
            [.. _terrariums
                .Where(terrarium => terrarium.UserId == ownerUserId)
                .OrderBy(terrarium => terrarium.Name)
                // The profile is always present once attached: every stored terrarium names a seeded profile.
                .Select(terrarium => AttachProfile(terrarium)!)]);

    public Task<Terrarium?> FindOwnedAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(AttachProfile(_terrariums.FirstOrDefault(
            terrarium => terrarium.Id == terrariumId
                         && terrarium.UserId == ownerUserId
                         && terrarium.DeletedAt == null)));

    /// <summary>
    /// Fixes up the profile navigation the way EF Core's <c>Include</c> does, so a terrarium the store just
    /// accepted comes back with its species name rather than an empty string.
    /// </summary>
    private Terrarium? AttachProfile(Terrarium? terrarium)
    {
        if (terrarium is { SpeciesProfile: null } && terrarium.SpeciesProfileId != Guid.Empty)
        {
            var profile = _profiles.FirstOrDefault(candidate => candidate.Id == terrarium.SpeciesProfileId);

            if (profile is not null)
            {
                terrarium.SpeciesProfile = profile;
            }
        }

        return terrarium;
    }

    public Task<bool> SpeciesProfileExistsAsync(Guid speciesProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.Any(profile => profile.Id == speciesProfileId));

    public void AddTerrarium(Terrarium terrarium) => _terrariums.Add(terrarium);

    public Task<IReadOnlyDictionary<Guid, TerrariumActivity>> SummariseActivityAsync(
        IReadOnlyCollection<Guid> terrariumIds,
        CancellationToken cancellationToken)
    {
        var activity = terrariumIds.ToDictionary(id => id, _ => TerrariumActivity.None);

        foreach (var terrariumId in terrariumIds)
        {
            var latest = _samples
                .Where(sample => sample.TerrariumId == terrariumId)
                .Select(sample => (DateTimeOffset?)sample.RecordedAt)
                .Max();

            var open = _alerts.Count(alert => alert.TerrariumId == terrariumId && alert.State != AlertState.Resolved);

            activity[terrariumId] = new TerrariumActivity(latest, open);
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, TerrariumActivity>>(activity);
    }

    public Task<IReadOnlyList<TelemetrySample>> RecentSamplesAsync(
        Guid terrariumId,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TelemetrySample>>(
            [.. _samples
                .Where(sample => sample.TerrariumId == terrariumId)
                .OrderByDescending(sample => sample.RecordedAt)
                .Take(take)]);

    public Task<IReadOnlyList<TelemetrySample>> SamplesInRangeAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TelemetrySample>>(
            [.. _samples
                .Where(sample => sample.TerrariumId == terrariumId
                                 && sample.RecordedAt >= fromUtc
                                 && sample.RecordedAt <= toUtc)
                .OrderBy(sample => sample.RecordedAt)
                // Mirrors the filtered include: only the requested metric comes back.
                .Select(sample => new TelemetrySample
                {
                    Id = sample.Id,
                    TerrariumId = sample.TerrariumId,
                    DeviceId = sample.DeviceId,
                    RecordedAt = sample.RecordedAt,
                    ReceivedAt = sample.ReceivedAt,
                    Sequence = sample.Sequence,
                    QualityFlags = sample.QualityFlags,
                    FirmwareVersion = sample.FirmwareVersion,
                    Source = sample.Source,
                    Readings = [.. sample.Readings.Where(reading => reading.Metric == metric)],
                })]);

    public Task<int> CountSamplesAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(_samples.Count(
            sample => sample.TerrariumId == terrariumId
                      && sample.RecordedAt >= fromUtc
                      && sample.RecordedAt <= toUtc));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Always commits. The adapter's false answer means "another writer moved the rowversion", which is a property
    /// of the database; a test that wants a stale-write refusal sets <see cref="RowVersionConflict"/> instead of
    /// pretending the fake is EF.
    /// </summary>
    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        if (RowVersionConflict)
        {
            return Task.FromResult(false);
        }

        SaveCount++;
        return Task.FromResult(true);
    }

    /// <summary>Makes the next <see cref="TrySaveChangesAsync"/> report a lost optimistic-concurrency race.</summary>
    public bool RowVersionConflict { get; set; }
}
