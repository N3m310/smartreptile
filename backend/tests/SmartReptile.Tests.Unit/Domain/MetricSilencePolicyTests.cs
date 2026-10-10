using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The silence window's rule of §02-design/05 §5 and the two questions the dispatcher asks of a stored window
/// (roadmap task 3.4).
/// </summary>
public class MetricSilencePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_inside_the_cap_with_a_reason_is_accepted() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddHours(4), "lamp replacement")
            .Should().Be(SilenceRefusal.None);

    [Fact]
    public void A_window_of_exactly_the_cap_is_accepted() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddHours(MetricSilence.MaxWindowHours), "away for the day")
            .Should().Be(SilenceRefusal.None, "the design's cap is a maximum, not an exclusive bound");

    [Fact]
    public void A_window_one_second_past_the_cap_is_refused() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddHours(MetricSilence.MaxWindowHours).AddSeconds(1), "away for a fortnight")
            .Should().Be(SilenceRefusal.WindowTooLong);

    [Fact]
    public void A_blank_reason_is_refused() =>
        MetricSilencePolicy.Validate(Now, Now.AddHours(1), "   ").Should().Be(SilenceRefusal.ReasonMissing);

    [Fact]
    public void A_missing_reason_is_refused_before_the_window_is_judged() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddDays(-1), null)
            .Should().Be(SilenceRefusal.ReasonMissing, "the reason is the field the client always sends");

    [Fact]
    public void A_reason_longer_than_the_column_is_refused() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddHours(1), new string('a', MetricSilence.ReasonMaxLength + 1))
            .Should().Be(SilenceRefusal.ReasonTooLong);

    [Fact]
    public void A_window_that_ends_now_is_refused() =>
        MetricSilencePolicy
            .Validate(Now, Now, "just in case")
            .Should().Be(SilenceRefusal.UntilNotInTheFuture, "a silence of nothing is most often a client clock behind");

    [Fact]
    public void A_window_that_already_ended_is_refused() =>
        MetricSilencePolicy
            .Validate(Now, Now.AddMinutes(-1), "yesterday")
            .Should().Be(SilenceRefusal.UntilNotInTheFuture);

    [Fact]
    public void A_window_is_active_before_its_end_and_not_after()
    {
        var silence = new MetricSilence { UntilUtc = Now.AddHours(1) };

        silence.IsActiveAt(Now).Should().BeTrue();
        silence.IsActiveAt(Now.AddHours(1)).Should().BeFalse("the end instant is not inside the window");
        silence.IsActiveAt(Now.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void A_cancelled_window_is_inactive_however_long_it_had_left()
    {
        var silence = new MetricSilence { UntilUtc = Now.AddHours(1), CancelledAt = Now.AddMinutes(-1) };

        silence.IsActiveAt(Now).Should().BeFalse();
    }

    [Fact]
    public void A_window_with_no_metric_covers_every_metric_and_a_named_one_covers_only_itself()
    {
        var all = new MetricSilence { Metric = null };
        var temperature = new MetricSilence { Metric = MetricCode.TempC };

        all.Covers(MetricCode.TempC).Should().BeTrue();
        all.Covers(MetricCode.LightLux).Should().BeTrue();
        temperature.Covers(MetricCode.TempC).Should().BeTrue();
        temperature.Covers(MetricCode.LightLux).Should().BeFalse();
    }
}
