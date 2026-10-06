using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// The <c>readingAdded</c> event of `07-appendices/03` §6 — roadmap task 2.9. The payload is a contract with the
/// app and the dashboard, so its shape is asserted here rather than only observed on the wire.
/// </summary>
public class ReadingAddedPayloadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_carries_the_sample_and_its_metrics()
    {
        var terrariumId = Guid.NewGuid();
        var sample = new PersistedSample(
            42,
            terrariumId,
            Guid.NewGuid(),
            Now,
            7,
            QualityFlags.CalibrationApplied,
            [
                new TelemetryReading(MetricCode.TempC, 27.5m, 27.9m),
                new TelemetryReading(MetricCode.HumidityPct, 61m, null),
            ]);

        var payload = ReadingAddedPayload.From(sample);

        payload.TerrariumId.Should().Be(terrariumId, "the event is pushed to that terrarium's group");
        payload.SampleId.Should().Be(42);
        payload.RecordedAt.Should().Be(Now);
        payload.Metrics.Should().HaveCount(2);
    }

    [Fact]
    public void From_keys_each_metric_the_way_the_rest_surface_does()
    {
        var sample = new PersistedSample(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now,
            1,
            QualityFlags.None,
            [new TelemetryReading(MetricCode.TempC, 27.5m, null)]);

        var metric = ReadingAddedPayload.From(sample).Metrics.Should().ContainSingle().Subject;

        // ApiKey rather than PayloadKey on purpose: one client keys both this event and a later history request
        // off the same string, and readings/latest answers with ApiKey.
        metric.Code.Should().Be(MetricDictionary.Get(MetricCode.TempC).ApiKey);
        metric.Value.Should().Be(27.5m, "the post-calibration value is what a keeper sees everywhere else");
        metric.Unit.Should().Be(MetricDictionary.Get(MetricCode.TempC).Unit);
    }

    [Fact]
    public void From_reports_the_samples_quality_bitmask_on_every_metric()
    {
        var flags = QualityFlags.SensorFault | QualityFlags.Backfilled;
        var sample = new PersistedSample(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now,
            1,
            flags,
            [
                new TelemetryReading(MetricCode.TempC, 27.5m, null),
                new TelemetryReading(MetricCode.LightLux, 120m, null),
            ]);

        var payload = ReadingAddedPayload.From(sample);

        // Quality is recorded per sample, so every metric of that sample carries the same bitmask.
        payload.Metrics.Should().OnlyContain(metric => metric.QualityFlags == (int)flags);
    }
}
