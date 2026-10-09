using FluentAssertions;
using SmartReptile.Application.Readings;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Terrariums;

/// <summary>
/// Task 2.8 — the terrarium read surface: list, detail, create, <c>readings/latest</c>, bucketed
/// <c>readings</c> and <c>coverage</c>. Covers TC-I-10 (bucket selection and gap handling) at the service level
/// and the parts of TC-I-11 and TC-I-13 that live here: ownership hiding, staleness, band and phase classification.
/// </summary>
public class TerrariumServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTerrariumStore _store = new();
    private readonly TestClock _clock = new() { UtcNow = Now };
    private readonly TerrariumService _service;
    private readonly Guid _owner = Guid.NewGuid();

    public TerrariumServiceTests() =>
        _service = new TerrariumService(_store, _clock, new TerrariumSettings("Asia/Ho_Chi_Minh", 3));

    // ---- live-update membership (FR-08, roadmap task 2.9) -------------------------------------------

    [Fact]
    public async Task IsMemberAsync_answers_true_for_the_owner_only()
    {
        var profile = _store.AddProfile();
        var mine = _store.AddTerrarium(_owner, "Alpha", profile);
        var someoneElses = _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        (await _service.IsMemberAsync(mine.Id, _owner)).Should().BeTrue();

        // This is what keeps the realtime hub from becoming a cross-tenant leak: the group name is derived from
        // the terrarium id, so joining one is asking to receive its data.
        (await _service.IsMemberAsync(someoneElses.Id, _owner)).Should().BeFalse();
        (await _service.IsMemberAsync(Guid.NewGuid(), _owner)).Should().BeFalse();
    }

    [Fact]
    public async Task IsMemberAsync_refuses_a_deleted_terrarium()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.DeletedAt = Now;

        (await _service.IsMemberAsync(terrarium.Id, _owner)).Should().BeFalse();
    }

    // ---- list and detail ----------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_only_the_callers_terrariums()
    {
        var profile = _store.AddProfile();
        var mine = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(mine, DeviceStatus.Online, Now);
        _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrariums.Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task List_reports_the_device_state_activity_and_species_name()
    {
        var profile = _store.AddProfile("Leopard gecko (semi-desert)");
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);
        _store.AddSample(terrarium.Id, device.Id, Now.AddMinutes(-1), default, (MetricCode.TempC, 28m));
        _store.AddAlert(terrarium.Id, device.Id);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);
        var summary = outcome.Terrariums.Should().ContainSingle().Subject;

        summary.SpeciesName.Should().Be("Leopard gecko (semi-desert)");
        summary.Device!.Status.Should().Be("online");
        summary.Device.SamplingIntervalSec.Should().Be(60);
        summary.LatestSampleAt.Should().Be(Now.AddMinutes(-1));
        summary.OpenAlertCount.Should().Be(1);
    }

    [Fact]
    public async Task List_reports_a_silent_device_as_offline()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // Three missed 60 s intervals is the documented threshold (FR-07 / BR-07.2).
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, lastSeenAt: Now.AddSeconds(-181));

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Terrariums!.Single().Device!.Status.Should().Be("offline");
    }

    [Fact]
    public async Task List_does_not_resurrect_a_revoked_device_as_offline()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Revoked, lastSeenAt: Now.AddDays(-30));

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Terrariums!.Single().Device!.Status.Should().Be("revoked");
    }

    [Fact]
    public async Task Get_of_another_accounts_terrarium_is_not_found()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.GetAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("not_found");
    }

    // ---- create -------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_refuses_a_blank_name_with_a_field_error()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("   ", profile.Id, null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("name");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_an_unknown_species_profile()
    {
        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", Guid.NewGuid(), null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Code.Should().Be("species_profile_not_found");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_an_unknown_time_zone()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, "Mars/Olympus_Mons"),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Field.Should().Be("timeZoneId");
    }

    [Fact]
    public async Task Create_persists_the_terrarium_and_falls_back_to_the_configured_time_zone()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("  Alpha  ", profile.Id, "  desk ", string.Empty, null),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        var created = outcome.Terrarium!;

        created.Name.Should().Be("Alpha", "names are trimmed");
        created.Location.Should().Be("desk");
        created.Description.Should().BeNull("an empty string is not a description");
        created.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        created.SpeciesName.Should().Be(profile.Name);
        created.LatestSampleAt.Should().BeNull();
        created.OpenAlertCount.Should().Be(0);

        _store.SaveCount.Should().Be(1);
        _store.Terrariums.Should().Contain(terrarium => terrarium.Id == created.Id && terrarium.UserId == _owner);
    }

    // ---- readings/latest ----------------------------------------------------------------------------

    [Fact]
    public async Task LatestReadings_reports_the_newest_value_per_metric_including_ones_the_newest_sample_lacks()
    {
        var (terrarium, device, profile) = Arrange();

        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now.AddMinutes(-5),
            default,
            (MetricCode.TempC, 28m),
            (MetricCode.LightLux, 500m));

        // Light has no reading in the newest sample — the sun went down — and must still keep its last value.
        _store.AddSample(terrarium.Id, device.Id, Now.AddMinutes(-1), default, (MetricCode.TempC, 29m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.LastSampleAt.Should().Be(Now.AddMinutes(-1));
        readings.Device!.DeviceId.Should().Be(device.PublicId);

        readings.Metrics.Should().HaveCount(2);
        readings.Metrics.Should().ContainSingle(metric => metric.Code == "tempC")
            .Which.Value.Should().Be(29m, "the newest sample wins");
        readings.Metrics.Should().ContainSingle(metric => metric.Code == "lightLux")
            .Which.CapturedAt.Should().Be(Now.AddMinutes(-5), "its own sample's timestamp travels with it");
    }

    [Fact]
    public async Task LatestReadings_orders_metrics_by_key_so_cards_keep_their_place()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            default,
            (MetricCode.TempC, 28m),
            (MetricCode.HumidityPct, 35m),
            (MetricCode.LightLux, 400m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Metrics.Select(metric => metric.Code)
            .Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(28, ReadingStatus.InRange)]
    [InlineData(33, ReadingStatus.OutOfRange)]
    [InlineData(40, ReadingStatus.Critical)]
    public async Task LatestReadings_classifies_a_value_against_the_effective_band(
        double value,
        string expected)
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            default,
            (MetricCode.TempC, (decimal)value));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Status.Should().Be(expected);
        metric.Target.Should().Be(new MetricBand(26m, 32m));
    }

    [Fact]
    public async Task LatestReadings_prefers_a_terrarium_override_over_the_profile_band()
    {
        var (terrarium, device, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 31m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Target.Should().Be(new MetricBand(28m, 30m));
        metric.Status.Should().Be(
            ReadingStatus.OutOfRange,
            "31 °C is inside the profile band but outside this terrarium's override");
    }

    [Fact]
    public async Task LatestReadings_uses_the_night_band_outside_the_photoperiod()
    {
        var (terrarium, device, profile) = Arrange();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);

        // The fake clock converts to UTC, so 23:00 is outside the 07:00–19:00 window.
        var night = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);
        _store.AddSample(terrarium.Id, device.Id, night, default, (MetricCode.TempC, 26m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Target.Should().Be(new MetricBand(20m, 24m), "the night band applies after lights-off");
        metric.Status.Should().Be(ReadingStatus.OutOfRange);
    }

    [Fact]
    public async Task LatestReadings_marks_a_faulted_sample_unavailable()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            QualityFlags.SensorFault,
            (MetricCode.TempC, 28m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Status.Should().Be(ReadingStatus.Unavailable);
        metric.QualityFlags.Should().Be((int)QualityFlags.SensorFault);
    }

    [Fact]
    public async Task LatestReadings_marks_an_implausible_value_unavailable_however_plausible_the_flags_look()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 85m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Metrics.Single().Status.Should().Be(ReadingStatus.Unavailable);
    }

    [Fact]
    public async Task LatestReadings_marks_a_maintenance_device_as_maintenance()
    {
        var (terrarium, device, _) = Arrange(deviceStatus: DeviceStatus.Maintenance);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 28m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.Metrics.Single().Status.Should().Be(ReadingStatus.Maintenance);
        readings.Device!.Status.Should().Be("maintenance");
    }

    [Fact]
    public async Task LatestReadings_is_empty_rather_than_null_timestamped_before_the_first_sample()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.LastSampleAt.Should().BeNull();
        readings.Metrics.Should().BeEmpty("both clients parse capturedAt unconditionally, so a metric without a sample is absent");
    }

    [Fact]
    public async Task LatestReadings_has_no_device_when_none_is_claimed()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Device.Should().BeNull();
        outcome.Readings.Metrics.Should().BeEmpty();
    }

    [Fact]
    public async Task LatestReadings_of_another_accounts_terrarium_is_not_found()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.LatestReadingsAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    // ---- effective thresholds (FR-10, BR-10.3, roadmap 3.1) ------------------------------------------

    [Fact]
    public async Task EffectiveThresholds_lists_every_configured_metric_with_the_layer_it_came_from()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Thresholds!.TerrariumId.Should().Be(terrarium.Id);
        outcome.Thresholds.CapturedAtUtc.Should().Be(Now);
        outcome.Thresholds.TimeZoneId.Should().Be("UTC");

        // Sorted by metric key, the same way the cards are, so the editor's rows do not shuffle between reloads.
        outcome.Thresholds.EffectiveThresholds.Select(entry => entry.Metric)
            .Should().Equal("humidityPct", "tempC");

        var temperature = outcome.Thresholds.EffectiveThresholds[1];
        temperature.Source.Should().Be("override");
        temperature.Phase.Should().Be("any");
        temperature.Unit.Should().Be("°C");
        temperature.TargetMin.Should().Be(28m);
        temperature.TargetMax.Should().Be(30m);

        var humidity = outcome.Thresholds.EffectiveThresholds[0];
        humidity.Source.Should().Be("profile");
        humidity.TargetMin.Should().Be(30m);
        humidity.TargetMax.Should().Be(40m);
    }

    [Fact]
    public async Task EffectiveThresholds_reports_the_phase_the_card_is_being_judged_in()
    {
        var (terrarium, _, _) = Arrange();
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);
        var temperature = outcome.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");

        temperature.Phase.Should().Be("night");
        temperature.TargetMin.Should().Be(20m);
        temperature.TargetMax.Should().Be(24m);
    }

    [Fact]
    public async Task EffectiveThresholds_omits_a_metric_that_has_no_band_for_this_phase()
    {
        var profile = _store.AddProfile();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        // Absent rather than present with null bounds: an absent row is what the editor unions with the dictionary
        // to show as unconfigured, and the evaluator treats it as unjudged.
        outcome.Thresholds!.EffectiveThresholds.Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveThresholds_of_another_accounts_terrarium_is_not_found()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.EffectiveThresholdsAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task EffectiveThresholds_of_a_missing_terrarium_is_not_found()
    {
        var outcome = await _service.EffectiveThresholdsAsync(Guid.NewGuid(), _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task EffectiveThresholds_agrees_with_the_band_the_card_is_classified_against()
    {
        var (terrarium, device, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 31m));

        var readings = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var thresholds = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        var card = readings.Readings!.Metrics.Single(metric => metric.Code == "tempC");
        var resolved = thresholds.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");

        // The whole point of sharing the resolver: an editor previewing this band is previewing the band the card
        // was coloured with, so the two cannot drift apart.
        card.Target.Should().Be(new MetricBand(resolved.TargetMin, resolved.TargetMax));
        card.Status.Should().Be(ReadingStatus.OutOfRange);
    }

    // ---- readings (range) ---------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "raw")]
    [InlineData(24, "5min")]
    [InlineData(24 * 30, "hourly")]
    public async Task Readings_echoes_the_bucket_the_range_width_selects(int hours, string expectedBucket)
    {
        var (terrarium, _, _) = Arrange();
        var from = Now.AddHours(-hours);

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            Now,
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be(expectedBucket);
    }

    [Fact]
    public async Task Readings_emits_every_bucket_so_a_gap_is_a_null_point_and_not_a_straight_line()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // Wider than six hours, so the series is five-minute buckets (BR-09.1) and the holes inside it are explicit.
        var to = from.AddHours(6).AddMinutes(10);

        _store.AddSample(terrarium.Id, device.Id, from, default, (MetricCode.TempC, 28m));
        _store.AddSample(terrarium.Id, device.Id, from.AddMinutes(20), default, (MetricCode.TempC, 30m));

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            to,
            CancellationToken.None);

        var series = outcome.Series!;

        series.Bucket.Should().Be("5min");
        series.Points.Should().HaveCount(74);
        series.Points[0].Should().Be(new ReadingPoint(from, 28m, 28m, 28m, 1));
        series.Points[4].Should().Be(new ReadingPoint(from.AddMinutes(20), 30m, 30m, 30m, 1));
        series.Points[1].Should().Be(
            new ReadingPoint(from.AddMinutes(5), null, null, null, 0),
            "an interval with no data is null with count 0 (BR-09.5)");
    }

    [Fact]
    public async Task Readings_aggregates_min_max_avg_and_count_per_bucket()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // Three samples inside one five-minute bucket of a window wide enough to be bucketed at all.
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(10), default, (MetricCode.TempC, 20m));
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(60), default, (MetricCode.TempC, 26m));
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(120), default, (MetricCode.TempC, 22m));

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            from.AddHours(6).AddMinutes(5),
            CancellationToken.None);

        var point = outcome.Series!.Points[0];

        point.T.Should().Be(from);
        point.Min.Should().Be(20m);
        point.Max.Should().Be(26m);
        point.Avg.Should().Be(22.667m);
        point.Count.Should().Be(3);
    }

    [Fact]
    public async Task Readings_degrades_to_five_minute_buckets_when_raw_would_break_the_point_budget()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // 721 samples inside a raw-width window: a 10 s device at the fastest allowed interval.
        for (var index = 0; index <= RangeQueryRules.MaxBucketPoints; index++)
        {
            _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(index * 10), default, (MetricCode.TempC, 28m));
        }

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            from.AddHours(2),
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be("5min");
        outcome.Series.Points.Should().HaveCountLessOrEqualTo(RangeQueryRules.MaxBucketPoints);
    }

    [Fact]
    public async Task Readings_never_exceeds_the_point_budget_even_when_the_window_is_not_bucket_aligned()
    {
        var (terrarium, _, _) = Arrange();

        // A range that starts 17 minutes past the hour: the first hourly bucket starts before the window, which is
        // exactly the case that would otherwise produce a 721st point (NFR-02, TC-I-10).
        var to = new DateTimeOffset(2026, 10, 3, 14, 17, 0, TimeSpan.Zero);
        var from = to.AddDays(-30);

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            to,
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be("hourly");
        outcome.Series.Points.Should().HaveCount(RangeQueryRules.MaxBucketPoints);
    }

    [Fact]
    public async Task Readings_refuses_a_range_longer_than_thirty_days()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            Now.AddDays(-31),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("range_too_large");
    }

    [Fact]
    public async Task Readings_refuses_a_reversed_range()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            Now,
            Now.AddHours(-1),
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("invalid_range");
    }

    [Fact]
    public async Task Readings_of_another_accounts_terrarium_is_not_found()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.ReadingsAsync(
            foreign.Id,
            _owner,
            MetricCode.TempC,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    // ---- coverage -----------------------------------------------------------------------------------

    [Fact]
    public async Task Coverage_compares_received_samples_with_what_the_interval_implies()
    {
        var (terrarium, device, _) = Arrange();

        // 60 s interval over one hour is 60 expected samples; 45 arrive.
        for (var index = 0; index < 45; index++)
        {
            _store.AddSample(terrarium.Id, device.Id, Now.AddHours(-1).AddSeconds(index * 60), default, (MetricCode.TempC, 28m));
        }

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        var coverage = outcome.Coverage!;

        coverage.SamplingIntervalSec.Should().Be(60);
        coverage.ExpectedSamples.Should().Be(60);
        coverage.ReceivedSamples.Should().Be(45);
        coverage.CoveragePct.Should().Be(75m);
    }

    [Fact]
    public async Task Coverage_without_a_bound_device_is_a_conflict()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("device_not_bound");
    }

    [Fact]
    public async Task Coverage_accepts_a_window_wider_than_the_chart_limit_but_inside_retention()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddDays(-60),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
    }

    /// <summary>A terrarium with a day/night temperature band pair, a bound online device and a species profile.</summary>
    private (Terrarium Terrarium, Device Device, SpeciesProfile Profile) Arrange(
        DeviceStatus deviceStatus = DeviceStatus.Online)
    {
        var profile = _store.AddProfile();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 20m, 24m, ThresholdPhase.Night);
        FakeTerrariumStore.AddBand(profile, MetricCode.HumidityPct, 30m, 40m);

        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, deviceStatus, Now);

        return (terrarium, device, profile);
    }

    // ---- update and delete (FR-03, roadmap 2.8's remaining half) -------------------------------------

    [Fact]
    public async Task Update_changes_the_field_it_was_given_and_leaves_the_rest_alone()
    {
        var profile = _store.AddProfile("Leopard gecko");
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.Location = "desk";
        terrarium.Description = "notes";
        var rowVersion = terrarium.RowVersion!;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Alpha renamed", null, null, null, null),
            _owner,
            rowVersion,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.Name.Should().Be("Alpha renamed");
        terrarium.Location.Should().Be("desk");
        terrarium.Description.Should().Be("notes");
        terrarium.UpdatedAt.Should().Be(Now);

        // The response carries a token: without one the client could not make a second edit.
        outcome.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_clears_a_free_text_field_when_it_is_sent_empty()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.Location = "desk";
        terrarium.Description = "notes";

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, string.Empty, string.Empty, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        // An omitted member means "leave it"; an empty string is the one way a JSON body can say "clear it".
        outcome.Succeeded.Should().BeTrue();
        terrarium.Location.Should().BeNull();
        terrarium.Description.Should().BeNull();
    }

    [Fact]
    public async Task Update_leaves_the_name_alone_when_it_is_omitted()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, "windowsill", null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Name.Should().Be("Alpha");
        terrarium.Location.Should().Be("windowsill");
    }

    [Fact]
    public async Task Update_refuses_a_stale_rowversion_and_writes_nothing()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var savesBefore = _store.SaveCount;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [9, 9, 9, 9, 9, 9, 9, 9],
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("precondition_failed");

        // The point of a concurrency token: the stale writer does not overwrite what it never read.
        terrarium.Name.Should().Be("Alpha");
        _store.SaveCount.Should().Be(savesBefore);
    }

    [Fact]
    public async Task Update_accepts_the_wildcard_rowversion()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // "If-Match: *" is existence-only, which the endpoint hands over as an empty token.
        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [],
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Update_reports_a_race_lost_at_commit_as_precondition_failed()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // The token matched when the row was read, and another writer moved it before the write landed — a second
        // window the read-time comparison cannot close, which is why the store reports it too.
        _store.RowVersionConflict = true;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("precondition_failed");
    }

    [Fact]
    public async Task Update_reports_a_foreign_terrarium_as_not_found()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Stolen", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        // Identical to "does not exist", so the route cannot be used to enumerate ids (BR-02.2).
        outcome.Problem!.Code.Should().Be("not_found");
        terrarium.Name.Should().Be("Someone else's");
    }

    [Fact]
    public async Task Update_validates_every_field_it_was_given()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(
                string.Empty,
                Guid.NewGuid(),
                new string('x', 121),
                new string('y', 1001),
                "Middle-earth/Shire"),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("validation_failed");

        outcome.Problem.Errors!.Select(violation => violation.Code).Should().BeEquivalentTo(
        [
            "terrarium_name_required",
            "species_profile_not_found",
            "terrarium_location_too_long",
            "terrarium_description_too_long",
            "terrarium_timezone_invalid",
        ]);
    }

    [Fact]
    public async Task Update_reports_a_missing_terrarium_as_not_found()
    {
        var outcome = await _service.UpdateAsync(
            Guid.NewGuid(),
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [1, 2, 3, 4, 5, 6, 7, 8],
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Delete_refuses_a_bound_device_without_the_flag()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("conflict_device_bound");
        terrarium.DeletedAt.Should().BeNull();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_with_the_flag_unbinds_the_device_and_soft_deletes()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: true, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.DeletedAt.Should().Be(Now);
        _store.SaveCount.Should().Be(1);

        // The board is released rather than orphaned: it keeps its credentials and owner, but ingest refuses an
        // unbound device, so it stops writing into an enclosure nobody can see.
        device.TerrariumId.Should().BeNull();
        device.Status.Should().Be(DeviceStatus.Provisioning);
        device.ProvisionedAt.Should().BeNull();
    }

    [Fact]
    public async Task Delete_deletes_an_unbound_terrarium_without_the_flag()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Delete_does_not_ask_about_a_revoked_device()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // A revoked device has already released the slot, so requiring the flag would make the owner delete twice
        // for no reason.
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Revoked, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_reports_a_missing_terrarium_as_not_found()
    {
        var outcome = await _service.DeleteAsync(Guid.NewGuid(), _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }
}
