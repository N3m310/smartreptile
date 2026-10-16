using FluentAssertions;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Identity;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Identity;

/// <summary>
/// Account management for the administrator (TASKS 2026-10-14, item 12). The two guards — no self-change, and never
/// the last usable administrator — are what this service exists for, so they are the first thing asserted.
/// </summary>
public class UserAdminServiceTests
{
    private readonly FakeUserStore _store = new();
    private readonly TestClock _clock = new();
    private readonly UserAdminService _users;
    private readonly Guid _admin = Guid.NewGuid();
    private readonly AuditActor _actor = new("203.0.113.7", "curl/8.5.0", "corr-admin");

    public UserAdminServiceTests()
    {
        _users = new UserAdminService(_store, new FakePasswordHasher(), _clock);
        Add("admin", SystemRoles.Admin, id: _admin);
    }

    // ---- list and read ------------------------------------------------------------------------------

    [Fact]
    public async Task List_pages_the_accounts_and_reports_the_total()
    {
        Add("alice", SystemRoles.Owner);
        Add("bob", SystemRoles.Technician);
        Add("carol", SystemRoles.Viewer);

        var outcome = await _users.ListAsync(search: null, skip: 1, take: 2);

        outcome.Succeeded.Should().BeTrue();
        outcome.Page!.Total.Should().Be(4);
        outcome.Page.Items.Should().HaveCount(2);
        outcome.Page.Skip.Should().Be(1);
        outcome.Page.Items.Select(item => item.Username).Should().Equal("alice", "bob");
    }

    [Fact]
    public async Task List_searches_username_and_email_case_insensitively()
    {
        Add("alice", SystemRoles.Owner);
        Add("bob", SystemRoles.Owner, email: "bob@example.test");

        var byName = await _users.ListAsync("ALI", null, null);
        var byEmail = await _users.ListAsync("BOB@EXAMPLE", null, null);

        byName.Page!.Items.Should().ContainSingle().Which.Username.Should().Be("alice");
        byEmail.Page!.Items.Should().ContainSingle().Which.Username.Should().Be("bob");
    }

    [Fact]
    public async Task List_clamps_the_page_size()
    {
        var outcome = await _users.ListAsync(null, skip: -5, take: 5_000);

        // A client cannot ask for the whole table in one call, and a negative skip is read as the first page rather
        // than as an error: neither is worth a refusal on a read.
        outcome.Page!.Skip.Should().Be(0);
        outcome.Page.Take.Should().Be(UserAdminService.MaxPageSize);
    }

    [Fact]
    public async Task Get_reports_an_unknown_account_distinctly()
    {
        var outcome = await _users.GetAsync(Guid.NewGuid());

        // Unlike the terrarium routes, this one may say so: the caller is the one account entitled to enumerate.
        outcome.Problem!.Code.Should().Be("user_not_found");
    }

    // ---- role changes -------------------------------------------------------------------------------

    [Fact]
    public async Task ChangeRole_promotes_an_account_and_audits_the_before_and_after()
    {
        var owner = Add("alice", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(owner, new UpdateUserRoleRequest("Admin"), _admin, _actor);

        outcome.Succeeded.Should().BeTrue();
        outcome.User!.Role.Should().Be(SystemRoles.Admin);
        _store.Users.Single(user => user.Id == owner).Role!.Code.Should().Be(SystemRoles.Admin);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.AccountRoleChanged);
        entry.EntityName.Should().Be(AuditEntity.User);
        entry.EntityId.Should().Be(owner.ToString());
        entry.UserId.Should().Be(_admin, "the actor is the administrator, not the account that changed");
        entry.BeforeJson.Should().Contain("Owner");
        entry.AfterJson.Should().Contain("Admin");
    }

    [Fact]
    public async Task ChangeRole_accepts_the_role_names_case_insensitively()
    {
        var owner = Add("alice", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(owner, new UpdateUserRoleRequest("technician"), _admin);

        outcome.Succeeded.Should().BeTrue();
        _store.Users.Single(user => user.Id == owner).Role!.Code.Should().Be(SystemRoles.Technician);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Superuser")]
    [InlineData("9")]
    public async Task ChangeRole_refuses_a_role_outside_the_vocabulary(string? role)
    {
        var owner = Add("alice", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(owner, new UpdateUserRoleRequest(role), _admin);

        outcome.Problem!.Code.Should().Be("role_invalid");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("role");
        _store.Users.Single(user => user.Id == owner).Role!.Code.Should().Be(SystemRoles.Owner);
        _store.AuditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRole_refuses_the_callers_own_account()
    {
        Add("alice", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(_admin, new UpdateUserRoleRequest("Viewer"), _admin);

        // An administrator who gave up their own role could not take it back: the route that would is behind the
        // role they just lost.
        outcome.Problem!.Code.Should().Be("self_change_refused");
        _store.Users.Single(user => user.Id == _admin).Role!.Code.Should().Be(SystemRoles.Admin);
        _store.AuditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRole_refuses_to_demote_the_only_usable_administrator()
    {
        // The target is the only usable administrator, and the caller is a different account — which is all the
        // guard looks at, because the route's own policy has already established that the caller may be here.
        var actor = Add("keeper", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(_admin, new UpdateUserRoleRequest("Viewer"), actor);

        outcome.Problem!.Code.Should().Be("last_administrator_refused");
        _store.Users.Single(user => user.Id == _admin).Role!.Code.Should().Be(SystemRoles.Admin);
        _store.AuditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRole_allows_a_demotion_once_a_second_administrator_is_usable()
    {
        var second = Add("root2", SystemRoles.Admin);
        var actor = Add("keeper", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(_admin, new UpdateUserRoleRequest("Viewer"), actor);

        // Two usable administrators exist, so removing one leaves the deployment able to author the catalogue.
        outcome.Succeeded.Should().BeTrue();
        _store.Users.Single(user => user.Id == _admin).Role!.Code.Should().Be(SystemRoles.Viewer);
        _store.Users.Single(user => user.Id == second).Role!.Code.Should().Be(SystemRoles.Admin);
    }

    [Fact]
    public async Task ChangeRole_to_the_role_it_already_has_writes_nothing()
    {
        var owner = Add("alice", SystemRoles.Owner);

        var outcome = await _users.ChangeRoleAsync(owner, new UpdateUserRoleRequest("Owner"), _admin);

        outcome.Succeeded.Should().BeTrue();
        _store.AuditEntries.Should().BeEmpty("nothing changed, so there is nothing to record");
        _store.SaveCount.Should().Be(0);
    }

    // ---- disable and re-enable ----------------------------------------------------------------------

    [Fact]
    public async Task Disable_ends_every_session_the_account_holds_and_audits_it()
    {
        var owner = Add("alice", SystemRoles.Owner);
        _store.AddRefreshToken(new RefreshToken { UserId = owner, FamilyId = Guid.NewGuid() });

        var outcome = await _users.SetDisabledAsync(owner, disabled: true, _admin, _actor);

        outcome.Succeeded.Should().BeTrue();
        outcome.User!.IsActive.Should().BeFalse();
        _store.Users.Single(user => user.Id == owner).DisabledAt.Should().Be(_clock.UtcNow);
        _store.Tokens.Should().OnlyContain(token => token.RevokedAt != null, "a disabled account keeps no session");

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.AccountDisabled);
        entry.UserId.Should().Be(_admin);
    }

    [Fact]
    public async Task Disable_and_enable_are_idempotent()
    {
        var owner = Add("alice", SystemRoles.Owner);

        await _users.SetDisabledAsync(owner, disabled: true, _admin);
        var disabledAt = _store.Users.Single(user => user.Id == owner).DisabledAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        (await _users.SetDisabledAsync(owner, disabled: true, _admin)).Succeeded.Should().BeTrue();

        _store.Users.Single(user => user.Id == owner).DisabledAt.Should().Be(disabledAt);
        _store.AuditEntries.Should().ContainSingle("a repeat changes nothing, so it is not recorded");

        (await _users.SetDisabledAsync(owner, disabled: false, _admin)).Succeeded.Should().BeTrue();
        _store.Users.Single(user => user.Id == owner).DisabledAt.Should().BeNull();
        _store.AuditEntries.Select(entry => entry.Action)
            .Should().Equal(AuditAction.AccountDisabled, AuditAction.AccountEnabled);
    }

    [Fact]
    public async Task Enable_of_an_account_that_was_never_disabled_writes_nothing()
    {
        var owner = Add("alice", SystemRoles.Owner);

        var outcome = await _users.SetDisabledAsync(owner, disabled: false, _admin);

        outcome.Succeeded.Should().BeTrue();
        _store.AuditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Disable_refuses_the_callers_own_account()
    {
        var outcome = await _users.SetDisabledAsync(_admin, disabled: true, _admin);

        outcome.Problem!.Code.Should().Be("self_change_refused");
        _store.Users.Single(user => user.Id == _admin).DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task Disable_refuses_the_last_usable_administrator()
    {
        var actor = Add("keeper", SystemRoles.Owner);

        var outcome = await _users.SetDisabledAsync(_admin, disabled: true, actorUserId: actor);

        outcome.Problem!.Code.Should().Be("last_administrator_refused");
        _store.Users.Single(user => user.Id == _admin).DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task Disable_reports_an_unknown_account_distinctly()
    {
        var outcome = await _users.SetDisabledAsync(Guid.NewGuid(), disabled: true, _admin);

        outcome.Problem!.Code.Should().Be("user_not_found");
    }

    [Fact]
    public async Task A_disabled_administrator_does_not_count_as_usable()
    {
        Add("root2", SystemRoles.Admin, disabled: _clock.UtcNow);
        var actor = Add("keeper", SystemRoles.Owner);

        // Two admin rows exist but only one is usable, so demoting the usable one is the change that would leave the
        // deployment without an administrator.
        var outcome = await _users.ChangeRoleAsync(_admin, new UpdateUserRoleRequest("Viewer"), actor);

        outcome.Problem!.Code.Should().Be("last_administrator_refused");
        _store.Users.Single(user => user.Id == _admin).Role!.Code.Should().Be(SystemRoles.Admin);
    }

    /// <summary>
    /// An account in the store, with the identifiers the list filters on. The role is taken from the store's own
    /// roles by code, so an account always carries the permissions the real seed would have given it.
    /// </summary>
    private Guid Add(
        string username,
        string roleCode,
        string? email = null,
        DateTimeOffset? disabled = null,
        Guid? id = null)
    {
        var role = _store.Roles.Single(candidate => candidate.Code == roleCode);

        var user = new User
        {
            Id = id ?? Guid.NewGuid(),
            Username = username,
            Email = email ?? $"{username}@example.test",
            RoleId = role.Id,
            Role = role,
            DisabledAt = disabled,
            CreatedAt = _clock.UtcNow,
        };

        _store.AddUser(user);
        return user.Id;
    }
}
