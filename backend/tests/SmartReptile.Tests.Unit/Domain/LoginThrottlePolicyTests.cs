using FluentAssertions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>BR-01.4 failed-login thresholds (§02-design/06 §2).</summary>
public class LoginThrottlePolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 19)]
    public void Allows_an_attempt_below_both_thresholds(int usernameFailures, int ipFailures) =>
        LoginThrottlePolicy.Evaluate(usernameFailures, ipFailures).IsLocked.Should().BeFalse();

    [Fact]
    public void Locks_the_identifier_at_the_username_threshold()
    {
        var decision = LoginThrottlePolicy.Evaluate(LoginThrottlePolicy.MaxFailuresPerUsername, 0);

        decision.IsLocked.Should().BeTrue();
        decision.Code.Should().Be("account_locked");
    }

    [Fact]
    public void Blocks_the_address_at_the_ip_threshold()
    {
        var decision = LoginThrottlePolicy.Evaluate(0, LoginThrottlePolicy.MaxFailuresPerIp);

        decision.IsLocked.Should().BeTrue();
        decision.Code.Should().Be("ip_blocked");
    }

    [Fact]
    public void Reports_the_identifier_lock_when_both_are_over_the_limit()
    {
        // The account lock is the answer the user can act on, so it wins over the address block.
        var decision = LoginThrottlePolicy.Evaluate(
            LoginThrottlePolicy.MaxFailuresPerUsername,
            LoginThrottlePolicy.MaxFailuresPerIp);

        decision.Code.Should().Be("account_locked");
    }

    [Fact]
    public void Defaults_match_the_documented_windows()
    {
        LoginThrottlePolicy.MaxFailuresPerUsername.Should().Be(5);
        LoginThrottlePolicy.UsernameWindowMinutes.Should().Be(15);
        LoginThrottlePolicy.MaxFailuresPerIp.Should().Be(20);
        LoginThrottlePolicy.IpWindowMinutes.Should().Be(15);
    }
}
