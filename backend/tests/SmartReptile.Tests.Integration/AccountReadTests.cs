using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The account reads against a real SQL Server. What a fake cannot catch is the eager loading: the account summary
/// answers "what can this account do" from <c>User.Role.Permissions</c>, so a read that loads the role but not its
/// grants reports an empty permission set for every row rather than failing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountReadTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task The_account_list_loads_each_accounts_grants()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db, SystemRoles.TechnicianId);

        var accounts = await new EfUserStore(db).ListAsync(search: null, skip: 0, take: 500, CancellationToken.None);

        var account = accounts.Should().ContainSingle(user => user.Id == userId).Subject;

        account.Role.Should().NotBeNull();
        account.Role!.Code.Should().Be(SystemRoles.Technician);
        account.Role.Permissions.Select(grant => grant.PermissionCode)
            .Should().BeEquivalentTo(
                SystemRoles.Seed.Single(role => role.Id == SystemRoles.TechnicianId).Permissions,
                "the list is where the console reads what an account may do, so the grants have to come with the role");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_single_account_reads_load_the_grants_too()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db, SystemRoles.TechnicianId);
        var store = new EfUserStore(db);

        var byId = await store.FindUserByIdAsync(userId, CancellationToken.None);
        var byUsername = await store.FindUserAsync(byId!.Username, CancellationToken.None);

        byId.Role!.Permissions.Should().NotBeEmpty();
        byUsername!.Role!.Permissions.Select(grant => grant.PermissionCode).Should().Contain(Permissions.AlertWrite);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_account_permission_count_counts_through_the_role_join()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var manager = await Seed.UserAsync(db, SystemRoles.AdminId);
        var keeper = await Seed.UserAsync(db, SystemRoles.OwnerId);
        var store = new EfUserStore(db);

        // The guard the two services share: "somebody else can still manage accounts".
        (await store.CountUsersWithPermissionAsync(Permissions.UserManage, excludingUserId: manager, CancellationToken.None))
            .Should().Be(0, "the keeper's role does not grant the account permissions");

        (await store.CountUsersWithPermissionAsync(Permissions.UserManage, excludingUserId: keeper, CancellationToken.None))
            .Should().BeGreaterThan(0);

        await tx.RollbackAsync();
    }
}
