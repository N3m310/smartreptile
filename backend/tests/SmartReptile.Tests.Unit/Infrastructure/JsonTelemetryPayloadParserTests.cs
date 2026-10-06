using System.Text;
using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Infrastructure.Ingest;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>
/// The payload adapter: bytes → document. Its contract is tolerance (a wrong field becomes a null the validator
/// judges, not an exception), so the tests assert what survives and what is reported rather than that it "parses".
/// </summary>
public class JsonTelemetryPayloadParserTests
{
    private const string Batch = """
        {
          "deviceId": "sr-3f9a2c",
          "seq": 10457,
          "fw": "1.2.0",
          "ts": "2026-09-21T08:15:00Z",
          "samples": [
            { "t": 0, "tf": 28.75, "rh": 41.20, "lux": 1820.5, "uvi": 0.30, "st": 31.20, "q": 8,
              "raw": { "tf": 28.90, "rh": 40.80, "lux": 1790 } },
            { "t": 60, "tf": 28.90, "rh": 41.35 }
          ],
          "health": { "rssi": -63, "up_s": 86400, "heap_kb": 142, "bat": null, "src": "mains" }
        }
        """;

    private readonly JsonTelemetryPayloadParser _parser = new();

    private PayloadParseResult Parse(string json) =>
        _parser.Parse(Encoding.UTF8.GetBytes(json), TelemetryValidationLimits.Default);

    [Fact]
    public void GivenTheDocumentedPayload_ThenEveryFieldIsRead()
    {
        var document = Parse(Batch).Document!;

        document.DeviceId.Should().Be("sr-3f9a2c");
        document.Sequence.Should().Be(10_457);
        document.FirmwareVersion.Should().Be("1.2.0");
        document.Timestamp.Should().Be("2026-09-21T08:15:00Z");

        document.Samples.Should().HaveCount(2);
        document.Samples![0].OffsetSeconds.Should().Be(0);
        document.Samples[0].Metrics["tf"].Should().Be(28.75m);
        document.Samples[0].Metrics["st"].Should().Be(31.20m);
        document.Samples[0].Quality.Should().Be(8);
        document.Samples[0].RawMetrics["lux"].Should().Be(1790m);
        document.Samples[1].OffsetSeconds.Should().Be(60);
        document.Samples[1].RawMetrics.Should().BeEmpty();

        document.Health.Should().NotBeNull();
        document.Health!.RssiDbm.Should().Be(-63);
        document.Health.UptimeSeconds.Should().Be(86_400);
        document.Health.FreeHeapKb.Should().Be(142);
        document.Health.BatteryPct.Should().BeNull();
        document.Health.PowerSource.Should().Be("mains");
    }

    [Fact]
    public void GivenAnUnknownMetricKey_ThenTheParserKeepsItForTheValidatorToJudge()
    {
        // Rule V-10 belongs to the validator. A parser that dropped unknown keys would make the rule untestable and
        // would leave nothing for the "unknown_metric" log line to name.
        var document = Parse("""{"deviceId":"sr-1","seq":1,"fw":"1","ts":"2026-09-21T08:15:00Z","samples":[{"t":0,"zz":1.0,"tf":20}]}""").Document!;

        document.Samples![0].Metrics.Should().ContainKey("zz");
    }

    [Fact]
    public void GivenANonNumericValueForAKey_ThenTheKeyIsReportedRatherThanGuessed()
    {
        var document = Parse("""{"deviceId":"sr-1","seq":1,"fw":"1","ts":"2026-09-21T08:15:00Z","samples":[{"t":0,"tf":"warm","rh":41}]}""").Document!;

        document.Samples![0].InvalidKeys.Should().ContainSingle().Which.Should().Be("tf");
        document.Samples[0].Metrics.Should().ContainKey("rh").And.NotContainKey("tf");
    }

    [Fact]
    public void GivenANonNumericDiagnosticValue_ThenItIsSkippedSilently()
    {
        // The raw block is for calibration review. Refusing a real measurement over a diagnostic field would be the
        // wrong trade, so a nonsense raw value simply means "no raw value".
        var document = Parse("""{"deviceId":"sr-1","seq":1,"fw":"1","ts":"2026-09-21T08:15:00Z","samples":[{"t":0,"tf":20,"raw":{"tf":"junk"}}]}""").Document!;

        document.Samples![0].RawMetrics.Should().BeEmpty();
        document.Samples[0].InvalidKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    public void GivenBytesThatAreNotABatchObject_ThenItIsRefusedAsSchemaInvalid(string payload)
    {
        var result = Parse(payload);

        result.Document.Should().BeNull();
        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public void GivenAPayloadOverTheSizeLimit_ThenItIsRefusedAsPayloadTooLarge()
    {
        var padding = new string('x', TelemetryValidationLimits.Default.MaxPayloadKb * 1024);
        var result = Parse($$"""{"deviceId":"sr-1","seq":1,"fw":"1","ts":"2026-09-21T08:15:00Z","samples":[{"t":0,"tf":20}],"pad":"{{padding}}"}""");

        result.Document.Should().BeNull();
        result.Problem!.Code.Should().Be("payload_too_large");
    }

    [Fact]
    public void GivenNoSamplesArray_ThenTheDocumentSurvivesWithNoSamples()
    {
        // Rule V-01 says a batch without samples is refused by the validator, which is where the refusal can carry
        // the same code as the other schema faults.
        var result = Parse("""{"deviceId":"sr-1","seq":1,"fw":"1","ts":"2026-09-21T08:15:00Z"}""");

        result.Document!.Samples.Should().BeNull();
    }

    [Fact]
    public void GivenASequenceTooLargeForAnInt32_ThenItIsStillRead()
    {
        // seq is an int64 on the wire: a long-lived device is expected to pass 2^31 and an overflow would look like
        // a schema fault rather than a counter.
        var document = Parse("""{"deviceId":"sr-1","seq":4294967296,"fw":"1","ts":"2026-09-21T08:15:00Z","samples":[{"t":0,"tf":20}]}""").Document!;

        document.Sequence.Should().Be(4_294_967_296);
    }
}
