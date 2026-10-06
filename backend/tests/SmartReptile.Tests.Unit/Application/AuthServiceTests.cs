using System.Text;
using FluentAssertions;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Security;

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
    private readonly FakeRecoveryCodeGenerator _recoveryCodes = new();
    private readonly FakePasswordResetNotifier _notifier = new();
    private readonly AuthService _auth;

    public AuthServiceTests() =>
        _auth = new AuthService(
            _store,
            new FakePasswordHasher(),
            new FakeAccessTokenService(_clock),
            _secrets,
            // The real primitive, not a fake: SHA-256 over a few bytes is cheaper than the fixture that would
            // stand in for it, and the recovery tests are about the flow, not about the hash.
            new Sha256SecretHasher(),
            _recoveryCodes,
            _notifier,
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

    // ---- recovery (BR-01.5) -------------------------------------------------------------------------

    [Fact]
    public async Task Register_returns_a_well_formed_recovery_code_and_stores_only_its_hash()
    {
        var outcome = await RegisterAsync();

        var code = outcome.RecoveryCode;
        code.Should().NotBeNull("the keeper has to leave registration with something to recover with");
        RecoveryCode.IsWellFormed(code).Should().BeTrue();
        code!.Length.Should().Be(RecoveryCode.DefaultLength);

        var user = _store.Users.Should().ContainSingle().Subject;
        user.RecoveryCodeHash.Should().HaveCount(32);
        user.RecoveryCodeSalt.Should().HaveCount(16);
        user.RecoveryCodeIssuedAt.Should().Be(_clock.UtcNow);

        // The plaintext must not be recoverable from what was stored: hashing it again cannot reproduce the row,
        // which is the property that makes a database dump worthless here.
        Encoding.UTF8.GetString(user.RecoveryCodeHash!).Should().NotContain(code);
    }

    [Fact]
    public async Task Register_with_a_taken_identifier_returns_a_code_that_cannot_verify()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();

        // Same shape on both paths, or the response would disclose whether the identifier was free.
        second.RecoveryCode.Should().NotBeNull();
        second.RecoveryCode.Should().NotBe(first.RecoveryCode);

        var existing = _store.Users.Should().ContainSingle().Subject;
        new Sha256SecretHasher()
            .Verify(second.RecoveryCode!, existing.RecoveryCodeHash!, existing.RecoveryCodeSalt!)
            .Should()
            .BeFalse("a decoy code must not open the account it was refused for");
    }

    [Fact]
    public async Task Recover_replaces_the_password_and_hands_back_a_replacement_code()
    {
        var code = (await RegisterAsync()).RecoveryCode!;
        var before = _store.Users.Single().RecoveryCodeHash;

        var outcome = await RecoverAsync(code);

        outcome.Succeeded.Should().BeTrue();
        outcome.RecoveryCode.Should().NotBeNull().And.NotBe(code, "the consumed code is rotated");

        _store.Users.Single().RecoveryCodeHash.Should().NotEqual(before);
        _store.Users.Single().RecoveryCodeIssuedAt.Should().Be(_clock.UtcNow);

        (await LoginAsync(password: NewPassword)).Succeeded.Should().BeTrue();
        (await LoginAsync()).Problem!.Code.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Recover_consumes_the_code_it_was_given()
    {
        var code = (await RegisterAsync()).RecoveryCode!;
        var replacement = (await RecoverAsync(code)).RecoveryCode!;

        var replay = await RecoverAsync(code);

        replay.Succeeded.Should().BeFalse();
        replay.Problem!.Code.Should().Be("invalid_recovery_code");

        // The replacement is the live one, so the flow is not a one-shot that dies with its first use.
        (await RecoverAsync(replacement)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Recover_accepts_a_lower_case_code_typed_with_separators()
    {
        var code = (await RegisterAsync()).RecoveryCode!;
        var typed = RecoveryCode.Format(code).ToLowerInvariant();

        var outcome = await RecoverAsync(typed);

        outcome.Succeeded.Should().BeTrue("a code copied off paper must survive being retyped");
    }

    [Fact]
    public async Task Recover_answers_identically_for_a_wrong_code_and_an_unknown_account()
    {
        await RegisterAsync();

        var wrongCode = await RecoverAsync("ZZZZZZZZZZZZZZZZZZZZ");
        var unknownAccount = await RecoverAsync("ZZZZZZZZZZZZZZZZZZZZ", identifier: "nobody");

        wrongCode.Problem!.Code.Should().Be("invalid_recovery_code");
        unknownAccount.Problem!.Code.Should().Be("invalid_recovery_code");
        wrongCode.Problem.Message.Should().Be(unknownAccount.Problem.Message);
    }

    [Fact]
    public async Task Recover_answers_identically_for_an_account_that_has_no_code_on_file()
    {
        // What an account created before recovery codes existed looks like.
        var legacy = FakePasswordHasher.DeriveAtCost(Password, FakePasswordHasher.TargetIterations);
        _store.AddUser(new User
        {
            Username = "legacy",
            Email = "legacy@example.com",
            PasswordHash = legacy.Hash,
            PasswordSalt = legacy.Salt,
            PasswordIterations = legacy.Iterations,
            Role = UserRole.Owner,
        });

        var outcome = await RecoverAsync("DEM2DEM2DEM2DEM2DEM2", identifier: "legacy");

        outcome.Problem!.Code.Should().Be("invalid_recovery_code");
        (await LoginAsync(identifier: "legacy")).Succeeded.Should().BeTrue("the account must be otherwise untouched");
    }

    [Fact]
    public async Task Recover_ends_every_existing_session()
    {
        var code = (await RegisterAsync()).RecoveryCode!;
        await LoginAsync();
        await LoginAsync();

        _store.Tokens.Should().HaveCount(2).And.OnlyContain(token => token.RevokedAt == null);

        (await RecoverAsync(code)).Succeeded.Should().BeTrue();

        _store.Tokens.Should().OnlyContain(
            token => token.RevokedAt != null,
            "whoever holds the forgotten password may still hold a live session");
    }

    [Fact]
    public async Task Recover_refuses_a_weak_password_without_spending_the_code()
    {
        var code = (await RegisterAsync()).RecoveryCode!;

        var refused = await RecoverAsync(code, password: "short");

        refused.Problem!.Code.Should().Be("password_policy_violation");
        refused.Problem.Errors.Select(violation => violation.Code).Should().Contain("password_too_short");

        // A policy refusal is not a credential guess, so it must not burn the code or the attempt budget.
        (await RecoverAsync(code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Recover_locks_the_identifier_after_repeated_failures()
    {
        var code = (await RegisterAsync()).RecoveryCode!;

        for (var attempt = 0; attempt < LoginThrottlePolicy.MaxFailuresPerUsername; attempt++)
        {
            (await RecoverAsync("ZZZZZZZZZZZZZZZZZZZZ")).Problem!.Code.Should().Be("invalid_recovery_code");
        }

        var locked = await RecoverAsync(code);

        locked.Problem!.Code.Should().Be(
            "account_locked",
            "the correct code must not be usable once the identifier is locked out");
    }

    // ---- server-issued reset codes (BR-01.5) ---------------------------------------------------------

    [Fact]
    public async Task Forgot_password_issues_a_code_and_hands_it_to_the_delivery_channel()
    {
        await RegisterAsync();

        var outcome = await ForgotPasswordAsync();

        outcome.Succeeded.Should().BeTrue();
        outcome.Session.Should().BeNull("issuing a code is not a session");

        var stored = _store.ResetCodes.Should().ContainSingle().Subject;
        stored.UserId.Should().Be(_store.Users.Single().Id);
        stored.CodeHash.Should().HaveCount(32);
        stored.ConsumedAt.Should().BeNull();
        stored.ExpiresAt.Should().Be(
            _clock.UtcNow.AddMinutes(new AuthSettings().PasswordResetCodeMinutes),
            "the code has to expire on its own, without anyone having to revoke it");
        stored.RequestedFromAddress.Should().Be(Ip);

        var delivery = _notifier.Last!;
        delivery.Username.Should().Be("linh");
        delivery.Email.Should().Be("linh@example.com");
        delivery.ExpiresAtUtc.Should().Be(stored.ExpiresAt);

        // The row must not let anyone read the code back out of the database.
        _secrets.Sha256(delivery.Code).Should().Equal(stored.CodeHash);
        Encoding.UTF8.GetString(stored.CodeHash).Should().NotContain(delivery.Code);
    }

    [Fact]
    public async Task Forgot_password_replaces_an_outstanding_code_rather_than_adding_one()
    {
        await RegisterAsync();

        await ForgotPasswordAsync();
        var first = _notifier.Last!.Code;

        await ForgotPasswordAsync();

        _store.ResetCodes.Should().HaveCount(2);
        _store.ResetCodes.Count(code => code.ConsumedAt is null).Should().Be(
            1,
            "at most one code is ever live for an account, so asking again closes the previous one");

        (await ResetPasswordAsync(first)).Problem!.Code.Should().Be(
            "invalid_reset_code",
            "the replaced code must stop working the moment a new one is issued");
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_way_for_an_unknown_identifier()
    {
        await RegisterAsync();

        var outcome = await ForgotPasswordAsync(identifier: "nobody");

        outcome.Succeeded.Should().BeTrue("the answer must not reveal whether the account exists");
        _store.ResetCodes.Should().BeEmpty("there is no account to issue a code for");
        _notifier.Deliveries.Should().BeEmpty("there is no address to deliver it to");
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_way_when_delivery_fails()
    {
        await RegisterAsync();
        _notifier.FailNextSend = true;

        var outcome = await ForgotPasswordAsync();

        outcome.Succeeded.Should().BeTrue(
            "a failing channel must not turn a known account into a 500 next to a 202 for an unknown one");
        _store.ResetCodes.Should().ContainSingle("the code was committed before delivery was attempted");
    }

    [Fact]
    public async Task Reset_password_sets_the_password_rotates_backup_and_ends_every_session()
    {
        var recovery = (await RegisterAsync()).RecoveryCode!;
        await LoginAsync();
        var delivery = await IssueResetCodeAsync();

        var outcome = await ResetPasswordAsync(delivery.Code);

        outcome.Succeeded.Should().BeTrue();
        outcome.RecoveryCode.Should().NotBeNull().And.NotBe(recovery, "the backup code rotates with the password");

        // The keeper has to be able to use the account with the password they just chose …
        (await LoginAsync(password: NewPassword)).Succeeded.Should().BeTrue();
        (await LoginAsync(password: Password)).Problem!.Code.Should().Be("invalid_credentials");

        // … with the rotated backup code …
        (await RecoverAsync(outcome.RecoveryCode!, password: Password)).Succeeded.Should().BeTrue();

        // … and with nobody left signed in under the old credentials.
        _store.Tokens.Where(token => token.RevokedAt is null).Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_password_consumes_the_code()
    {
        await RegisterAsync();
        var delivery = await IssueResetCodeAsync();

        (await ResetPasswordAsync(delivery.Code)).Succeeded.Should().BeTrue();

        var replay = await ResetPasswordAsync(delivery.Code);

        replay.Succeeded.Should().BeFalse();
        replay.Problem!.Code.Should().Be("invalid_reset_code");
    }

    [Fact]
    public async Task Reset_password_refuses_an_expired_code()
    {
        await RegisterAsync();
        var delivery = await IssueResetCodeAsync();

        _clock.Advance(TimeSpan.FromMinutes(new AuthSettings().PasswordResetCodeMinutes + 1));

        var outcome = await ResetPasswordAsync(delivery.Code);

        outcome.Problem!.Code.Should().Be("invalid_reset_code");
    }

    [Fact]
    public async Task Reset_password_refuses_a_code_issued_to_another_account()
    {
        await RegisterAsync();
        await RegisterAsync("other", "other@example.com");
        await ForgotPasswordAsync(identifier: "other");

        var outcome = await ResetPasswordAsync(_notifier.Last!.Code, identifier: "linh");

        outcome.Problem!.Code.Should().Be(
            "invalid_reset_code",
            "a code is only ever worth the account it was issued for");
        (await LoginAsync(password: Password)).Succeeded.Should().BeTrue("nothing was reset");
    }

    [Fact]
    public async Task Reset_password_answers_the_same_way_for_an_unknown_identifier_or_a_malformed_code()
    {
        await RegisterAsync();
        var delivery = await IssueResetCodeAsync();

        (await ResetPasswordAsync(delivery.Code, identifier: "nobody")).Problem!.Code
            .Should().Be("invalid_reset_code");
        (await ResetPasswordAsync("ZZZZZZZZZZZZZZZZZZZZ")).Problem!.Code
            .Should().Be("invalid_reset_code");
        (await ResetPasswordAsync("too-short")).Problem!.Code
            .Should().Be("invalid_reset_code");
    }

    [Fact]
    public async Task Reset_password_refuses_a_weak_password_without_burning_the_code()
    {
        await RegisterAsync();
        var delivery = await IssueResetCodeAsync();

        var refused = await ResetPasswordAsync(delivery.Code, password: "short");

        refused.Problem!.Code.Should().Be("password_policy_violation");
        refused.Problem.Errors.Select(violation => violation.Code).Should().Contain("password_too_short");

        (await ResetPasswordAsync(delivery.Code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_password_locks_the_identifier_after_repeated_failures()
    {
        await RegisterAsync();
        var delivery = await IssueResetCodeAsync();

        for (var attempt = 0; attempt < LoginThrottlePolicy.MaxFailuresPerUsername; attempt++)
        {
            (await ResetPasswordAsync("ZZZZZZZZZZZZZZZZZZZZ")).Problem!.Code.Should().Be("invalid_reset_code");
        }

        var locked = await ResetPasswordAsync(delivery.Code);

        locked.Problem!.Code.Should().Be(
            "account_locked",
            "the correct code must not be usable once the identifier is locked out");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private Task<AuthOutcome> RegisterAsync(
        string username = "linh",
        string email = "linh@example.com",
        string password = Password) =>
        _auth.RegisterAsync(new RegisterRequest(username, email, password, null));

    private Task<AuthOutcome> LoginAsync(string identifier = "linh", string password = Password) =>
        _auth.LoginAsync(new LoginRequest(identifier, password, "unit-test"), Ip);

    private Task<AuthOutcome> ForgotPasswordAsync(string identifier = "linh") =>
        _auth.ForgotPasswordAsync(new ForgotPasswordRequest(identifier), Ip);

    private Task<AuthOutcome> ResetPasswordAsync(
        string resetCode,
        string identifier = "linh",
        string password = NewPassword) =>
        _auth.ResetPasswordAsync(new ResetPasswordRequest(identifier, resetCode, password), Ip);

    private async Task<PasswordResetDelivery> IssueResetCodeAsync(string identifier = "linh")
    {
        var outcome = await ForgotPasswordAsync(identifier);
        outcome.Succeeded.Should().BeTrue();

        return _notifier.Last!;
    }

    private Task<AuthOutcome> RecoverAsync(
        string recoveryCode,
        string identifier = "linh",
        string password = NewPassword) =>
        _auth.RecoverAsync(new RecoverRequest(identifier, recoveryCode, password), Ip);

    private async Task<AuthSession> RegisterAndLoginAsync()
    {
        await RegisterAsync();

        var outcome = await LoginAsync();

        outcome.Succeeded.Should().BeTrue();
        return outcome.Session!;
    }
}
