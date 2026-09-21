using FluentAssertions;
using SmartReptile.Domain.Summaries;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// 1-minute grid construction: interpolation across short gaps, honest nulls across long ones.
/// The gap behaviour is a product requirement (FR-09 BR-09.5) — a chart that interpolates a 4-hour outage
/// would show a straight line that never happened.
/// </summary>
public class MinuteGridTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenSamplesOneMinuteApart_ThenEveryMinuteIsFilled()
    {
        var samples = new (DateTimeOffset, decimal?)[]
        {
            (Start, 28m),
            (Start.AddMinutes(1), 29m),
            (Start.AddMinutes(2), 30m),
        };

        var grid = MinuteGrid.Build(samples, Start, Start.AddMinutes(3));

        grid.Should().HaveCount(3);
        grid.Select(c => c.Value).Should().Equal(28m, 29m, 30m);
    }

    [Fact]
    public void GivenTwoMinuteGap_ThenValuesAreLinearlyInterpolated()
    {
        var samples = new (DateTimeOffset, decimal?)[]
        {
            (Start, 28m),
            (Start.AddMinutes(2), 30m),
        };

        var grid = MinuteGrid.Build(samples, Start, Start.AddMinutes(2));

        grid[0].Value.Should().Be(28m);
        grid[1].Value.Should().Be(29m);
    }

    [Fact]
    public void GivenGapLongerThanTheTolerance_ThenCellsAreNull()
    {
        var samples = new (DateTimeOffset, decimal?)[]
        {
            (Start, 28m),
            (Start.AddHours(4).AddMinutes(-1), 31m),
        };

        var grid = MinuteGrid.Build(samples, Start, Start.AddHours(4));

        // Only the two endpoints carry data; nothing in between is invented (BR-09.5).
        grid.Count(c => c.Value.HasValue).Should().Be(2);
        grid.Skip(1).Take(grid.Count - 2).Should().OnlyContain(c => c.Value == null);
    }

    [Fact]
    public void GivenOnlyOlderSamples_ThenTheValueIsHeldOnlyInsideTheInterpolationTolerance()
    {
        var samples = new (DateTimeOffset, decimal?)[] { (Start, 28m) };

        var grid = MinuteGrid.Build(samples, Start, Start.AddMinutes(10));

        // Minutes 0-3 are within the 3-minute tolerance (hold), minutes 4-9 are honesty gaps (null).
        grid.Take(4).Should().OnlyContain(c => c.Value == 28m);
        grid.Skip(4).Should().OnlyContain(c => c.Value == null);
    }

    [Fact]
    public void GivenEmptyInput_ThenTheGridIsAllNull()
    {
        var grid = MinuteGrid.Build([], Start, Start.AddMinutes(5));

        grid.Should().HaveCount(5);
        grid.Should().OnlyContain(c => c.Value == null);
    }
}
