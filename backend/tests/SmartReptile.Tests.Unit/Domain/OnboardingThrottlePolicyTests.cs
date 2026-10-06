using FluentAssertions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>The self-registration anti-abuse limits of §02-design/06 §5, and the fact that they come from config.</summary>
public class OnboardingThrottlePolicyTests
{
    private static readonly OnboardingLimits Limits = OnboardingLimits.Documented;

    [Fact]
    public void Allows_the_first_attempt_from_an_address() =>
        OnboardingThrottlePolicy.Evaluate(0, 0, Limits).IsThrottled.Should().BeFalse();

    [Fact]
    public void Refuses_a_second_attempt_from_the_same_address_inside_the_window()
    {
        var decision = OnboardingThrottlePolicy.Evaluate(1, 1, Limits);

        decision.IsThrottled.Should().BeTrue();
        decision.Code.Should().Be("rate_limited");
    }

    [Fact]
    public void Refuses_once_the_global_hourly_cap_is_reached()
    {
        // A distributed flood: every address looks clean, the global counter does not.
        var decision = OnboardingThrottlePolicy.Evaluate(
            ipAttemptsInWindow: 0,
            globalAttemptsInWindow: Limits.MaxAttemptsPerHourGlobal,
            Limits);

        decision.IsThrottled.Should().BeTrue();
        decision.Code.Should().Be("rate_limited");
    }

    [Fact]
    public void Follows_a_limit_that_configuration_lowered()
    {
        // The limits are configuration, not constants: a demo that wants a stricter cap must get one.
        var strict = Limits with { MaxAttemptsPerHourGlobal = 2 };

        OnboardingThrottlePolicy.Evaluate(0, 1, Limits).IsThrottled.Should().BeFalse();
        OnboardingThrottlePolicy.Evaluate(0, 1, strict).IsThrottled.Should().BeFalse();
        OnboardingThrottlePolicy.Evaluate(0, 2, strict).IsThrottled.Should().BeTrue();
    }

    [Fact]
    public void The_documented_limits_are_pinned()
    {
        OnboardingLimits.Documented.MaxAttemptsPerIp.Should().Be(1);
        OnboardingLimits.Documented.IpWindowMinutes.Should().Be(5);
        OnboardingLimits.Documented.MaxAttemptsPerHourGlobal.Should().Be(20);
        OnboardingLimits.Documented.GlobalWindowMinutes.Should().Be(60);
    }
}
