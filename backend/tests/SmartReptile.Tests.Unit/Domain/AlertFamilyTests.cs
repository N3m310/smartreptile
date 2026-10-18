using FluentAssertions;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Evaluation;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The two alert families (TASKS 2026-10-14, item 1). The split is a product rule, so the expectations here are
/// written out per metric rather than derived from the dictionary: a test that reads the mapping it is checking
/// would pass whatever the mapping became.
/// </summary>
public class AlertFamilyTests
{
    [Theory]
    [InlineData(MetricCode.TempC, AlertFamily.Species)]
    [InlineData(MetricCode.UvIndex, AlertFamily.Species)]
    [InlineData(MetricCode.SurfaceTempC, AlertFamily.Species)]
    [InlineData(MetricCode.HumidityPct, AlertFamily.Enclosure)]
    [InlineData(MetricCode.LightLux, AlertFamily.Enclosure)]
    [InlineData(MetricCode.BatteryPct, AlertFamily.Enclosure)]
    [InlineData(MetricCode.RssiDbm, AlertFamily.Enclosure)]
    public void Every_metric_belongs_to_the_family_the_product_assigns_it(MetricCode metric, AlertFamily expected)
    {
        MetricDictionary.Get(metric).Family.Should().Be(expected);
    }

    [Fact]
    public void The_two_families_cover_the_whole_dictionary()
    {
        // A metric added without a family would silently inherit the first enumeration member, which is how a new
        // temperature probe would end up on the enclosure screen.
        MetricDictionary.All.Select(definition => definition.Family)
            .Distinct()
            .Should().BeEquivalentTo(AlertFamilies.All);
    }

    [Fact]
    public void A_device_level_alert_is_the_enclosures_business()
    {
        // Silence, a failed probe and a skewed clock have no metric, and the board's condition is a statement about
        // the enclosure rather than about the animal in it.
        Alert.FamilyFor(null).Should().Be(AlertFamily.Enclosure);
    }

    [Theory]
    [InlineData("enclosure", AlertFamily.Enclosure)]
    [InlineData("Enclosure", AlertFamily.Enclosure)]
    [InlineData("SPECIES", AlertFamily.Species)]
    public void The_api_key_is_parsed_case_insensitively(string key, AlertFamily expected)
    {
        AlertFamilies.TryParseApiKey(key, out var family).Should().BeTrue();
        family.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("habitat")]
    public void An_unknown_key_is_not_parsed(string? key)
    {
        // Refused rather than defaulted: showing one family's rows under the other family's heading is worse than
        // an error the client has to fix.
        AlertFamilies.TryParseApiKey(key, out _).Should().BeFalse();
    }

    [Fact]
    public void Every_family_has_a_key_of_its_own()
    {
        AlertFamilies.All.Select(AlertFamilies.ApiKey)
            .Should().Equal("enclosure", "species");
    }

    [Fact]
    public void A_silent_board_opens_an_enclosure_alert()
    {
        var device = new Device { PublicId = "sr-1a2b3c", DeviceName = "Node" };

        var alert = DeviceSilenceAlertWriter.Open(
            device,
            Guid.NewGuid(),
            AlertSeverity.Warning,
            DateTimeOffset.UnixEpoch);

        alert.Family.Should().Be(AlertFamily.Enclosure);
        alert.Metric.Should().BeNull();
    }

    [Theory]
    [InlineData(MetricCode.TempC, AlertFamily.Species)]
    [InlineData(MetricCode.HumidityPct, AlertFamily.Enclosure)]
    public void A_sensor_fault_takes_the_family_of_the_metric_it_measures(MetricCode metric, AlertFamily expected)
    {
        var device = new Device { PublicId = "sr-1a2b3c", DeviceName = "Node" };

        var alert = DeviceSignalAlertWriter.OpenSensorFault(
            device,
            Guid.NewGuid(),
            metric,
            DateTimeOffset.UnixEpoch);

        alert.Family.Should().Be(expected);
    }

    [Fact]
    public void A_clock_skew_entry_is_the_enclosures_business()
    {
        var device = new Device { PublicId = "sr-1a2b3c", DeviceName = "Node" };

        DeviceSignalAlertWriter
            .OpenClockSkew(device, Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .Family.Should().Be(AlertFamily.Enclosure);
    }

    [Theory]
    [InlineData(MetricCode.TempC, "species")]
    [InlineData(MetricCode.HumidityPct, "enclosure")]
    public void A_threshold_episode_takes_the_family_of_its_metric(MetricCode metric, string expectedKey)
    {
        var alert = ThresholdAlertWriter.Open(
            Band(metric),
            Context(),
            Sample(),
            (metric, ThresholdPhase.Any),
            ThresholdDecision.Open(AlertSeverity.Warning, DateTimeOffset.UnixEpoch),
            triggeringValue: 99m,
            peakValue: 99m);

        AlertFamilies.ApiKey(alert.Family).Should().Be(expectedKey);
    }

    [Fact]
    public void The_pushed_event_carries_the_family()
    {
        // A client routes a banner by family, so the realtime payload has to name it rather than make the client
        // look the metric up in a table of its own.
        var alert = new Alert
        {
            TerrariumId = Guid.NewGuid(),
            Metric = MetricCode.SurfaceTempC,
            Family = AlertFamily.Species,
        };

        AlertChangedPayload.From(alert, AlertEvent.Opened).Family.Should().Be("species");
    }

    /// <summary>A band wide enough that the episode under test is judged on the values it is given.</summary>
    /// <param name="metric">Metric the band is for.</param>
    private static ThresholdBand Band(MetricCode metric) => new()
    {
        Metric = metric,
        Phase = ThresholdPhase.Any,
        TargetMin = 20m,
        TargetMax = 30m,
        DwellWarnMinutes = 1,
        DwellCritMinutes = 2,
        RecoveryMargin = 0.5m,
        Enabled = true,
    };

    /// <summary>An enclosure with no bands of its own and no open alerts, for a writer that only reads its fields.</summary>
    private static EvaluationContext Context() => new(
        Guid.NewGuid(),
        "Asia/Ho_Chi_Minh",
        new TimeOnly(7, 0),
        12m,
        [],
        [],
        [],
        []);

    /// <summary>The sample that opened the episode.</summary>
    private static PersistedSample Sample() => new(
        SampleId: 1,
        TerrariumId: Guid.NewGuid(),
        DeviceId: Guid.NewGuid(),
        RecordedAt: DateTimeOffset.UnixEpoch,
        Sequence: 1,
        Flags: QualityFlags.None,
        Readings: []);
}
