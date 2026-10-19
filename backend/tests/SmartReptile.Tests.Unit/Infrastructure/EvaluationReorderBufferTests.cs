using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Evaluation;

namespace SmartReptile.Tests.Unit.Infrastructure;

public sealed class EvaluationReorderBufferTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("TestCase", "TC-U-60")]
    public void Releases_samples_in_recorded_time_order_after_the_window()
    {
        var buffer = new EvaluationReorderBuffer(Window);
        var later = Sample(sampleId: 2, deviceId: Guid.NewGuid(), Start.AddSeconds(20), sequence: 2);
        var earlier = Sample(sampleId: 1, deviceId: later.DeviceId, Start.AddSeconds(10), sequence: 1);
        buffer.Add([later, earlier]);

        buffer.ReleaseReady(Start.AddSeconds(39)).ReadySamples.Should().BeEmpty();

        var firstRelease = buffer.ReleaseReady(Start.AddSeconds(40));

        firstRelease.LateSampleCount.Should().Be(0);
        firstRelease.ReadySamples.Should().ContainSingle().Which.Should().Be(earlier);

        var secondRelease = buffer.ReleaseReady(Start.AddSeconds(50));
        secondRelease.ReadySamples.Should().ContainSingle().Which.Should().Be(later);
        buffer.PendingCount.Should().Be(0);
    }

    [Fact]
    [Trait("TestCase", "TC-U-60")]
    public void Keeps_device_ordering_independent_and_skips_samples_behind_the_watermark()
    {
        var buffer = new EvaluationReorderBuffer(Window);
        var firstDevice = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondDevice = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = Sample(1, firstDevice, Start, 1);
        var second = Sample(2, secondDevice, Start.AddSeconds(5), 1);
        buffer.Add([second, first]);

        var released = buffer.ReleaseReady(Start.AddSeconds(35));
        released.ReadySamples.Should().ContainInOrder(first, second);

        var late = Sample(3, firstDevice, Start.AddSeconds(-1), 2);
        var inOrder = Sample(4, firstDevice, Start.AddSeconds(6), 3);
        buffer.Add([late, inOrder]);

        var next = buffer.ReleaseReady(Start.AddSeconds(40));

        next.LateSampleCount.Should().Be(1);
        next.ReadySamples.Should().ContainSingle().Which.Should().Be(inOrder);
    }

    private static PersistedSample Sample(long sampleId, Guid deviceId, DateTimeOffset at, long sequence) =>
        new(
            sampleId,
            Guid.NewGuid(),
            deviceId,
            at,
            sequence,
            QualityFlags.None,
            []);
}
