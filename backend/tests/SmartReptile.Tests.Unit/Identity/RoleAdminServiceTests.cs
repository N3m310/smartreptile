using FluentAssertions;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Identity;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Identity;

/// <summary>
/// The role/permission matrix as data (TASKS 2026-10-14, item 12). The point of the table is that an administrator
/// can change it, so the tests are about the two things that must stay true while they do: a system role cannot be
/// deleted, and the last grant of the account permissions cannot be removed by anybody.
/// </summary>
public class RoleAdminServiceTests
{
    private readonly FakeUserStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RoleAdminService _roles;
    private readonly Guid _actor = Guid.NewGuid();
    private readonly AuditActor _request = new("203.0.113.7", "curl/8.5.0", "corr-role");

    public RoleAdminServiceTests() => _roles = new RoleAdminService(_store, _clock);

    // ---- reads --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_carries_every_shipped_role_with_its_grants()
    {
        var outcome = await _roles.ListAsync();

        outcome.Succeeded.Should().BeTrue();
        outcome.Roles!.Select(role => role.Code)
            .Should().BeEquivalentTo(SystemRoles.Owner, SystemRoles.Technician, SystemRoles.Viewer, SystemRoles.Admin);
        outcome.Roles.Should().OnlyContain(role => role.IsSystem);
    }

    [Fact]
    public async Task The_administrators_cross_account_permissions_are_the_two_any_codes()
    {
        // This is the answer to "which data does this role map to": the grants that reach past an account are a
        // property of the permission, and the role summary derives them rather than restating them.
        var admin = await _roles.GetAsync(SystemRoles.AdminId);

        admin.Role!.CrossAccountPermissions
            .Should().BeEquivalentTo(Permissions.DataReadAny, Permissions.DataWriteAny);
    }

    [Fact]
    public async Task Only_the_administrator_role_reaches_past_its_own_account()
    {
        var outcome = await _roles.ListAsync();

        outcome.Roles!
            .Where(role => role.Code != SystemRoles.Admin)
            .Should().OnlyContain(role => role.CrossAccountPermissions.Count == 0);
    }

    [Fact]
    public async Task A_roles_grants_are_reported_in_catalogue_order()
    {
        var outcome = await _roles.GetAsync(SystemRoles.AdminId);

        var expected = Permissions.All.Select(definition => definition.Code);
        outcome.Role!.Permissions.Should().Equal(expected);
    }

    [Fact]
    public async Task The_catalogue_offers_every_permission_with_its_group()
    {
        var outcome = await _roles.CatalogAsync();

        outcome.Succeeded.Should().BeTrue();
        outcome.Catalog!.Select(permission => permission.Code)
            .Should().Equal(Permissions.All.Select(definition => definition.Code));
        outcome.Catalog.Should().OnlyContain(permission => permission.Group.Length > 0);
    }

    [Fact]
    public async Task An_unknown_role_is_not_found_rather_than_empty()
    {
        var outcome = await _roles.GetAsync(Guid.NewGuid());

        outcome.Problem!.Code.Should().Be("role_not_found");
    }

    // ---- creating -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_reports_how_many_accounts_hold_each_role()
    {
        // The matrix draws this figure, and a delete is refused with it — a zero for a role somebody holds would be
        // an answer an administrator acts on.
        _store.AddUser(Holding(SystemRoles.AdminId));
        _store.AddUser(Holding(SystemRoles.AdminId));
        _store.AddUser(Holding(SystemRoles.ViewerId));

        var outcome = await _roles.ListAsync();

        outcome.Roles!.Single(role => role.Id == SystemRoles.AdminId).UserCount.Should().Be(2);
        outcome.Roles.Single(role => role.Id == SystemRoles.ViewerId).UserCount.Should().Be(1);
        outcome.Roles.Single(role => role.Id == SystemRoles.OwnerId).UserCount.Should().Be(0);
    }

    [Fact]
    public async Task A_custom_role_is_created_below_the_shipped_four_and_audited()
    {
        var outcome = await _roles.CreateAsync(
            new CreateRoleRequest("Auditor", "Auditor", "Reads everything, writes nothing", [Permissions.TerrariumRead]),
            _actor,
            _request);

        outcome.Succeeded.Should().BeTrue();
        outcome.Role!.IsSystem.Should().BeFalse("only the four the product ships are system roles");
        outcome.Role.Rank.Should().Be(RoleAdminService.DefaultRank);
        outcome.Role.Permissions.Should().Equal(Permissions.TerrariumRead);
        outcome.Role.UserCount.Should().Be(0);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.RoleCreated);
        entry.EntityName.Should().Be(AuditEntity.Role);
        entry.EntityId.Should().Be(outcome.Role.Id.ToString());
        entry.UserId.Should().Be(_actor);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("owner")]
    public async Task A_code_another_role_uses_is_refused_case_insensitively(string code)
    {
        var outcome = await _roles.CreateAsync(new CreateRoleRequest(code, "Copy", null, null), _actor);

        outcome.Problem!.Code.Should().Be("role_invalid");
        outcome.Problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("role_code_taken");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a code")]
    [InlineData("has/slash")]
    public async Task A_code_that_is_not_an_identifier_is_refused(string code)
    {
        var outcome = await _roles.CreateAsync(new CreateRoleRequest(code, "Name", null, null), _actor);

        outcome.Problem!.Errors.Should().Contain(violation => violation.Code == "role_code_invalid");
    }

    [Fact]
    public async Task A_permission_outside_the_catalogue_is_refused_rather_than_dropped()
    {
        // Ignoring it would create the role the caller asked for minus a grant, which is the one outcome an
        // administrator cannot notice from the answer.
        var outcome = await _roles.CreateAsync(
            new CreateRoleRequest("Auditor", "Auditor", null, [Permissions.TerrariumRead, "terrarium.burn"]),
            _actor);

        outcome.Problem!.Code.Should().Be("role_invalid");
        var violation = outcome.Problem.Errors.Should().ContainSingle().Subject;
        violation.Field.Should().Be("permissions");
        violation.Message.Should().Contain("terrarium.burn");
        _store.Roles.Should().NotContain(role => role.Code == "Auditor");
    }

    [Fact]
    public async Task An_empty_name_is_refused()
    {
        var outcome = await _roles.CreateAsync(new CreateRoleRequest("Auditor", "  ", null, null), _actor);

        outcome.Problem!.Errors.Should().Contain(violation => violation.Code == "role_name_invalid");
    }

    // ---- editing ------------------------------------------------------------------------------------

    [Fact]
    public async Task Editing_replaces_the_grant_set_rather_than_merging_it()
    {
        var before = await _roles.GetAsync(SystemRoles.ViewerId);

        var outcome = await _roles.UpdateAsync(
            SystemRoles.ViewerId,
            new UpdateRoleRequest(null, null, [Permissions.TerrariumRead, Permissions.AuditRead]),
            _actor,
            _request);

        outcome.Succeeded.Should().BeTrue();
        outcome.Role!.Permissions.Should().BeEquivalentTo(Permissions.TerrariumRead, Permissions.AuditRead);
        before.Role!.Permissions.Should().NotContain(Permissions.AuditRead, "the set is replaced, not merged");

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.RoleUpdated);
        entry.BeforeJson.Should().Contain(Permissions.NotificationRead);
        entry.AfterJson.Should().NotContain(Permissions.NotificationRead);
    }

    [Fact]
    public async Task A_shipped_roles_name_may_be_reworded_but_its_code_may_not()
    {
        var outcome = await _roles.UpdateAsync(
            SystemRoles.TechnicianId,
            new UpdateRoleRequest("Người trực", null, null),
            _actor,
            _request);

        outcome.Succeeded.Should().BeTrue();
        outcome.Role!.Name.Should().Be("Người trực");
        outcome.Role.Code.Should().Be(SystemRoles.Technician);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_writes_nothing()
    {
        var current = (await _roles.GetAsync(SystemRoles.ViewerId)).Role!;

        var outcome = await _roles.UpdateAsync(
            SystemRoles.ViewerId,
            new UpdateRoleRequest(current.Name, null, current.Permissions),
            _actor,
            _request);

        outcome.Succeeded.Should().BeTrue();
        _store.AuditEntries.Should().BeEmpty("an edit with no change is not an event");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task The_last_grant_of_the_account_permissions_cannot_be_removed()
    {
        // Nobody holds a role granting `user.manage` but Admin, so emptying Admin's set would leave a deployment with
        // no way to manage accounts — the same dead end the account service refuses one route over.
        var outcome = await _roles.UpdateAsync(
            SystemRoles.AdminId,
            new UpdateRoleRequest(null, null, [Permissions.TerrariumRead]),
            _actor,
            _request);

        outcome.Problem!.Code.Should().Be("last_administrator_refused");
        (await _roles.GetAsync(SystemRoles.AdminId)).Role!.Permissions.Should().Contain(Permissions.UserManage);
    }

    [Fact]
    public async Task The_account_permissions_may_move_to_another_role_that_somebody_holds()
    {
        var keeper = await _roles.GetAsync(SystemRoles.OwnerId);

        // An account holding Owner is what makes the removal safe: the permission survives the edit.
        _store.AddUser(Holding(SystemRoles.OwnerId));

        var outcome = await _roles.UpdateAsync(
            SystemRoles.AdminId,
            new UpdateRoleRequest(null, null, [.. keeper.Role!.Permissions, Permissions.UserManage]),
            _actor,
            _request);

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_permission_on_an_edit_is_refused()
    {
        var outcome = await _roles.UpdateAsync(
            SystemRoles.ViewerId,
            new UpdateRoleRequest(null, null, ["not.a.permission"]),
            _actor,
            _request);

        outcome.Problem!.Errors.Should().Contain(violation => violation.Code == "permission_unknown");
    }

    // ---- deleting -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_role_the_product_ships_cannot_be_deleted()
    {
        foreach (var systemRole in new[] { SystemRoles.OwnerId, SystemRoles.TechnicianId, SystemRoles.ViewerId, SystemRoles.AdminId })
        {
            var outcome = await _roles.DeleteAsync(systemRole, _actor, _request);

            outcome.Problem!.Code.Should().Be("system_role_immutable");
        }

        _store.Roles.Should().HaveCount(SystemRoles.Seed.Count);
    }

    [Fact]
    public async Task A_custom_role_somebody_holds_is_refused_with_the_number_of_holders()
    {
        var custom = (await _roles.CreateAsync(new CreateRoleRequest("Auditor", "Auditor", null, null), _actor)).Role!;
        _store.AddUser(Holding(custom.Id));

        var outcome = await _roles.DeleteAsync(custom.Id, _actor, _request);

        outcome.Problem!.Code.Should().Be("role_in_use");
        outcome.Problem.Message.Should().Contain("1");
    }

    [Fact]
    public async Task An_unused_custom_role_is_deleted_and_audited()
    {
        var custom = (await _roles.CreateAsync(
            new CreateRoleRequest("Auditor", "Auditor", null, [Permissions.AuditRead]),
            _actor)).Role!;

        var outcome = await _roles.DeleteAsync(custom.Id, _actor, _request);

        outcome.Succeeded.Should().BeTrue();
        _store.Roles.Should().NotContain(role => role.Id == custom.Id);

        var entry = _store.AuditEntries
            .Where(candidate => candidate.Action == AuditAction.RoleDeleted)
            .Should().ContainSingle().Subject;
        entry.BeforeJson.Should().Contain(Permissions.AuditRead);
    }

    [Fact]
    public async Task An_unknown_role_cannot_be_deleted()
    {
        var outcome = await _roles.DeleteAsync(Guid.NewGuid(), _actor);

        outcome.Problem!.Code.Should().Be("role_not_found");
    }

    /// <summary>An account holding a role, which is what the two guards count.</summary>
    private static User Holding(Guid roleId) => new()
    {
        Username = $"holder-{Guid.NewGuid():N}",
        Email = $"{Guid.NewGuid():N}@example.test",
        RoleId = roleId,
    };
}
