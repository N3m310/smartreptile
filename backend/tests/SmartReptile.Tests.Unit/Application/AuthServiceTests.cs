using FluentAssertions;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Application;

/// <summary>
/// The FR-01 use cases. Covers the assertions that belong to unit tests — hashing and rehash-on-login (TC-U-32),
/// refresh rotation and reuse detection (TC-U-34) and login throttling (TC-U-35) — against fakes for every port,
/// so no case touches a database, a socket or a real PBKDF2 cost.
/// </summary>
public class AuthServiceTests
{
    // Throwaway values for a local account, and deliberately low-entropy. CI's secret scanner flags
    // realistic-looking credentials wherever they appear — fixtures included — and allow-listing test paths is
    // exactly how a real key ends up committed unnoticed, so the fixtures keep the scan honest instead.
    private const string Password = "local-demo-1";
    private const string NewPassword = "local-demo-2";
    private const string WrongPassword = "local-demo-99";
    private const string Ip = "203.0.113.7";

    private readonly TestClock _clock = new();
    private readonly FakeUserStore _store = new();
    private readonly FakeLoginThrottleStore _throttle = new();
    private readonly FakeSecretGenerator _secrets = new();
    private readonly AuthService _auth;

    public AuthServiceTests() =>
        _auth = new AuthService(
            _store,
            new FakePasswordHasher(),
            new FakeAccessTokenService(_clock),
            _secrets,
            _throttle,
            _clock,
            new AuthSettings());

    // ---- registration -------------------------------------------------------------------------------

    [Fact]
    public async Task Register_stores_a_hashed_owner_and_issues_no_session()
    {
        var outcome = await RegisterAsync("Linh", "Linh@Example.COM");

        outcome.Succeeded.Should().BeTrue();
        outcome.Session.Should().BeNull("login is the only path that issues a session");

        var user = _store.Users.Should().ContainSingle().Subject;
        user.Username.Should().Be("linh");
        user.Email.Should().Be("linh@example.com");
        user.Role.Should().Be(UserRole.Owner);
        user.PasswordIterations.Should().Be(FakePasswordHasher.TargetIterations);
        user.PasswordHash.Should().HaveCount(32);
        user.PasswordSalt.Should().HaveCount(16);
    }

    [Fact]
    public async Task Register_answers_the_same_way_when_the_identifier_is_taken()
    {
        await RegisterAsync();

        var again = await RegisterAsync();

        again.Succeeded.Should().BeTrue("a taken identifier must not be distinguishable from a free one");
        _store.Users.Should().HaveCount(1);
    }

    [Fact]
    public async Task Register_reports_every_violation_and_creates_nothing()
    {
        var outcome = await RegisterAsync("L", "not-an-email", "short");

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("registration_invalid");
        outcome.Problem.Errors.Select(violation => violation.Code)
            .Should().Contain(["username_too_short", "email_invalid", "password_too_short"]);
        _store.Users.Should().BeEmpty();
    }

    // ---- login --------------------------------------------------------------------------------------

    [Fact]
    public async Task Login_with_the_correct_password_returns_a_session_and_records_the_login()
    {
        await RegisterAsync();

        var outcome = await LoginAsync();

        outcome.Succeeded.Should().BeTrue();
        outcome.Session!.User.Username.Should().Be("linh");
        outcome.Session.User.Role.Should().Be("Owner");
        outcome.Session.AccessToken.Should().NotBeNullOrEmpty();
        outcome.Session.RefreshToken.Should().NotBeNullOrEmpty();
        outcome.Session.AccessTokenExpiresAtUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
        outcome.Session.RefreshTokenExpiresAtUtc.Should().Be(_clock.UtcNow.AddDays(30));
        _store.Users.Single().LastLoginAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Login_answers_identically_for_a_wrong_password_and_an_unknown_account()
    {
        await RegisterAsync();

        var wrongPassword = await LoginAsync(password: WrongPassword);
        var unknownAccount = await LoginAsync(identifier: "nobody");

        wrongPassword.Problem!.Code.Should().Be("invalid_credentials");
        unknownAccount.Problem!.Code.Should().Be("invalid_credentials");
        wrongPassword.Problem.Message.Should().Be(unknownAccount.Problem.Message);
    }

    [Fact]
    public async Task Login_upgrades_a_password_hash_stored_at_a_lower_cost()
    {
        var stale = FakePasswordHasher.DeriveAtCost(Password, iterations: 100_000);

        _store.AddUser(new User
        {
            Username = "linh",
            Email = "linh@example.com",
            PasswordHash = stale.Hash,
            PasswordSalt = stale.Salt,
            PasswordIterations = stale.Iterations,
            Role = UserRole.Owner,
        });

        var outcome = await LoginAsync();

        outcome.Succeeded.Should().BeTrue();
        _store.Users.Single().PasswordIterations.Should().Be(FakePasswordHasher.TargetIterations);
    }

    [Fact]
    public async Task Login_locks_the_identifier_after_the_failure_threshold()
    {
        await RegisterAsync();

        for (var attempt = 0; attempt < LoginThrottlePolicy.MaxFailuresPerUsername; attempt++)
        {
            var failure = await LoginAsync(password: WrongPassword);
            failure.Problem!.Code.Should().Be("invalid_credentials");
        }

        var locked = await LoginAsync();

        locked.Problem!.Code.Should().Be("account_locked");
    }

    [Fact]
    public async Task Login_refuses_a_disabled_account()
    {
        await RegisterAsync();
        _store.Users.Single().DisabledAt = _clock.UtcNow;

        var outcome = await LoginAsync();

        outcome.Problem!.Code.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task A_successful_login_clears_the_failure_counter()
    {
        await RegisterAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await LoginAsync(password: WrongPassword);
        }

        (await LoginAsync()).Succeeded.Should().BeTrue();

        _throttle.GetWindow("linh", Ip, _clock.UtcNow.AddMinutes(-LoginThrottlePolicy.UsernameWindowMinutes))
            .UsernameFailures.Should().Be(0);
    }

    // ---- refresh ------------------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_rotates_the_token_within_the_same_family()
    {
        var session = await RegisterAndLoginAsync();

        var outcome = await _auth.RefreshAsync(session.RefreshToken);

        outcome.Succeeded.Should().BeTrue();
        outcome.Session!.RefreshToken.Should().NotBe(session.RefreshToken);
        _store.Tokens.Should().HaveCount(2);
        _store.Tokens.Select(token => token.FamilyId).Distinct().Should().HaveCount(1);
        _store.Tokens.Should().ContainSingle(token => token.ConsumedAt != null);
    }

    [Fact]
    public async Task Refresh_reuse_revokes_the_whole_family()
    {
        var session = await RegisterAndLoginAsync();
        var rotated = (await _auth.RefreshAsync(session.RefreshToken)).Session!;

        var reuse = await _auth.RefreshAsync(session.RefreshToken);

        reuse.Problem!.Code.Should().Be("token_reused");
        _store.Tokens.Should().OnlyContain(token => token.RevokedAt != null);

        var afterRevocation = await _auth.RefreshAsync(rotated.RefreshToken);
        afterRevocation.Problem!.Code.Should().Be("refresh_token_invalid");
    }

    [Fact]
    public async Task Refresh_rejects_an_unknown_token()
    {
        var outcome = await _auth.RefreshAsync("never-issued");

        outcome.Problem!.Code.Should().Be("refresh_token_invalid");
    }

    [Fact]
    public async Task Refresh_rejects_an_expired_token()
    {
        var session = await RegisterAndLoginAsync();
        _clock.Advance(TimeSpan.FromDays(31));

        var outcome = await _auth.RefreshAsync(session.RefreshToken);

        outcome.Problem!.Code.Should().Be("refresh_token_invalid");
    }

    // ---- logout -------------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_revokes_the_token_and_is_idempotent()
    {
        var session = await RegisterAndLoginAsync();

        (await _auth.LogoutAsync(session.RefreshToken)).Succeeded.Should().BeTrue();
        _store.Tokens.Should().OnlyContain(token => token.RevokedAt != null);

        (await _auth.LogoutAsync(session.RefreshToken)).Succeeded.Should().BeTrue();
        (await _auth.LogoutAsync("never-issued")).Succeeded.Should().BeTrue();

        (await _auth.RefreshAsync(session.RefreshToken)).Problem!.Code.Should().Be("refresh_token_invalid");
    }

    // ---- change password ----------------------------------------------------------------------------

    [Fact]
    public async Task ChangePassword_requires_the_current_password()
    {
        var session = await RegisterAndLoginAsync();

        var outcome = await _auth.ChangePasswordAsync(
            session.User.Id,
            new ChangePasswordRequest(WrongPassword, NewPassword));

        outcome.Problem!.Code.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task ChangePassword_applies_the_password_policy()
    {
        var session = await RegisterAndLoginAsync();

        var outcome = await _auth.ChangePasswordAsync(
            session.User.Id,
            new ChangePasswordRequest(Password, "short"));

        outcome.Problem!.Code.Should().Be("password_policy_violation");
        outcome.Problem.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ChangePassword_switches_the_password_and_ends_every_session()
    {
        var session = await RegisterAndLoginAsync();

        var outcome = await _auth.ChangePasswordAsync(
            session.User.Id,
            new ChangePasswordRequest(Password, NewPassword));

        outcome.Succeeded.Should().BeTrue();
        _store.Tokens.Should().OnlyContain(token => token.RevokedAt != null);
        (await LoginAsync(password: NewPassword)).Succeeded.Should().BeTrue();
        (await LoginAsync()).Problem!.Code.Should().Be("invalid_credentials");
    }

    // ---- profile ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetProfile_returns_the_account_or_nothing()
    {
        var session = await RegisterAndLoginAsync();

        var profile = await _auth.GetProfileAsync(session.User.Id);

        profile.Should().NotBeNull();
        profile!.Username.Should().Be("linh");
        (await _auth.GetProfileAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private Task<AuthOutcome> RegisterAsync(
        string username = "linh",
        string email = "linh@example.com",
        string password = Password) =>
        _auth.RegisterAsync(new RegisterRequest(username, email, password, null));

    private Task<AuthOutcome> LoginAsync(string identifier = "linh", string password = Password) =>
        _auth.LoginAsync(new LoginRequest(identifier, password, "unit-test"), Ip);

    private async Task<AuthSession> RegisterAndLoginAsync()
    {
        await RegisterAsync();

        var outcome = await LoginAsync();

        outcome.Succeeded.Should().BeTrue();
        return outcome.Session!;
    }
}
