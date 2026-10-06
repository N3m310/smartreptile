using FluentAssertions;
using SmartReptile.Application.Readings;

namespace SmartReptile.Tests.Unit.Application.Readings;

/// <summary>
/// TC-I-10 — the bucketing rule of FR-09 (BR-09.1), asserted without a database because it is arithmetic on the
/// requested width. The point budget matters as much as the bucket: a chart endpoint that can return a whole
/// retention window is a chart endpoint that times out (NFR-01, NFR-02).
/// </summary>
public class RangeQueryRulesTests
{
    [Theory]
    [InlineData(1, ReadingBucket.Raw)]
    [InlineData(6, ReadingBucket.Raw)]
    [InlineData(7, ReadingBucket.FiveMinutes)]
    [InlineData(24, ReadingBucket.FiveMinutes)]
    [InlineData(48, ReadingBucket.FiveMinutes)]
    [InlineData(49, ReadingBucket.Hourly)]
    [InlineData(24 * 30, ReadingBucket.Hourly)]
    public void ChooseBucket_follows_the_documented_widths(int hours, ReadingBucket expected) =>
        RangeQueryRules.ChooseBucket(TimeSpan.FromHours(hours)).Should().Be(expected);

    [Fact]
    public void The_widest_accepted_range_is_exactly_the_point_budget_in_hourly_buckets() =>
        (RangeQueryRules.MaxChartWidth / RangeQueryRules.BucketSize(ReadingBucket.Hourly)!.Value)
            .Should().Be(RangeQueryRules.MaxBucketPoints);

    [Fact]
    public void The_widest_five_minute_range_is_within_the_point_budget() =>
        (RangeQueryRules.FiveMinuteMaxWidth / RangeQueryRules.BucketSize(ReadingBucket.FiveMinutes)!.Value)
            .Should().BeLessOrEqualTo(RangeQueryRules.MaxBucketPoints);

    [Fact]
    public void Raw_has_no_bucket_size_because_a_point_is_a_sample() =>
        RangeQueryRules.BucketSize(ReadingBucket.Raw).Should().BeNull();

    [Fact]
    public void Bucket_names_are_the_wire_spelling_the_clients_echo()
    {
        RangeQueryRules.BucketName(ReadingBucket.Raw).Should().Be("raw");
        RangeQueryRules.BucketName(ReadingBucket.FiveMinutes).Should().Be("5min");
        RangeQueryRules.BucketName(ReadingBucket.Hourly).Should().Be("hourly");
    }

    [Fact]
    public void TryValidateWindow_refuses_a_range_that_does_not_advance()
    {
        RangeQueryRules.TryValidateWindow(
                new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
                RangeQueryRules.MaxChartWidth,
                "30 days",
                out var code,
                out var message)
            .Should().BeFalse();

        code.Should().Be("invalid_range");
        message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryValidateWindow_refuses_a_range_past_the_width_limit()
    {
        RangeQueryRules.TryValidateWindow(
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero),
                RangeQueryRules.MaxChartWidth,
                "30 days",
                out var code,
                out var message)
            .Should().BeFalse();

        code.Should().Be("range_too_large");
        message.Should().Contain("30 days", "the refusal has to say what the limit is");
    }

    [Fact]
    public void TryValidateWindow_accepts_a_window_at_the_limit()
    {
        var to = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        RangeQueryRules.TryValidateWindow(
                to - RangeQueryRules.MaxChartWidth,
                to,
                RangeQueryRules.MaxChartWidth,
                "30 days",
                out var code,
                out var message)
            .Should().BeTrue();

        code.Should().BeNull();
        message.Should().BeNull();
    }
}
