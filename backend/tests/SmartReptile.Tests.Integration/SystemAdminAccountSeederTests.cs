using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The system administrator start-up step (FR-10). Against a real SQL Server, because what it has to get right is
/// the row it writes: a hash the login path can verify, the <see cref="SystemRoles.Admin"/> role whose permissions
/// the authorisation policies read, and a second start-up that changes nothing.
/// </summary>
/// <remarks>
/// Every test rolls its transaction back, so no account outlives the case that created it — the database is shared
/// by the whole collection.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SystemAdminAccountSeederTests(DatabaseFixture fixture)
{
    private const string Password = "Gecko#Cage7";

    [Fact]
    public async Task The_configured_administrator_is_created_with_a_credential_the_hasher_accepts()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var username = Unique("admin");
        var created = await Seeder(db, username, password: Password).SeedAsync();

        created.Should().BeTrue();

        var row = await db.Users.SingleAsync(user => user.Username == username);

        row.RoleId.Should().Be(
            SystemRoles.AdminId,
            "the account permissions are the Admin role's grants, and the token is minted from them");
        row.Email.Should().Be($"{username}@example.test");
        row.PasswordHash.Should().NotBeEmpty();
        row.RecoveryCodeHash.Should().BeNull(
            "a start-up path that printed a recovery code would be a live credential in a log");

        new Pbkdf2PasswordHasher()
            .Verify(Password, new PasswordHash(row.PasswordHash, row.PasswordSalt, row.PasswordIterations))
            .Should().BeTrue("the account exists to be signed into");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_second_start_up_creates_nothing()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var username = Unique("admin");
        (await Seeder(db, username, Password).SeedAsync()).Should().BeTrue();

        (await Seeder(db, username, Password).SeedAsync()).Should().BeFalse("the step is idempotent");

        (await db.Users.CountAsync(user => user.Username == username)).Should().Be(1);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_configuration_owns_the_password_of_an_account_it_created()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var username = Unique("admin");
        await Seeder(db, username, Password).SeedAsync();

        await Seeder(db, username, "Gecko#Cage8").SeedAsync();

        var row = await db.Users.SingleAsync(user => user.Username == username);
        var hasher = new Pbkdf2PasswordHasher();
        var stored = new PasswordHash(row.PasswordHash, row.PasswordSalt, row.PasswordIterations);

        hasher.Verify("Gecko#Cage8", stored).Should().BeTrue(
            "editing Admin:Password and restarting is the documented rotation and the only reset path");
        hasher.Verify(Password, stored).Should().BeFalse();

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_credential_the_login_path_would_refuse_is_not_written()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var username = Unique("admin");

        (await Seeder(db, username, "weak").SeedAsync()).Should().BeFalse("PasswordPolicy refuses it");

        (await db.Users.CountAsync(user => user.Username == username)).Should().Be(0,
            "an administrator who cannot sign in looks like one that can, which is worse than none");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task An_account_that_merely_shares_the_name_is_left_alone()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var username = Unique("admin");
        var owner = new User
        {
            Username = username,
            Email = $"{username}@example.test",
            PasswordHash = [1, 2, 3],
            PasswordSalt = [4, 5, 6],
            RoleId = SystemRoles.OwnerId,
        };

        db.Users.Add(owner);
        await db.SaveChangesAsync();

        (await Seeder(db, username, Password).SeedAsync()).Should().BeFalse(
            "a keeper who happens to hold the configured name keeps their account");

        var row = await db.Users.SingleAsync(user => user.Username == username);

        row.RoleId.Should().Be(SystemRoles.OwnerId);
        row.PasswordHash.Should().Equal(owner.PasswordHash, "nothing about the row may be rewritten");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task No_configuration_means_no_administrator()
    {
        await using var db = fixture.CreateContext();

        var seeder = new SystemAdminAccountSeeder(
            db,
            new Pbkdf2PasswordHasher(),
            Options.Create(new AdminAccountOptions()),
            NullLogger<SystemAdminAccountSeeder>.Instance);

        (await seeder.SeedAsync()).Should().BeFalse(
            "the section is also the switch: a deployment that names no administrator has nothing to seed");
    }

    /// <summary>A seeder configured for one case, over the caller's context so the row joins its transaction.</summary>
    private static SystemAdminAccountSeeder Seeder(
        SmartReptileDbContext db,
        string username,
        string password) =>
        new(
            db,
            new Pbkdf2PasswordHasher(),
            Options.Create(new AdminAccountOptions
            {
                Username = username,
                Email = $"{username}@example.test",
                Password = password,
            }),
            NullLogger<SystemAdminAccountSeeder>.Instance);

    /// <summary>A name no other run of this suite holds, so cases cannot collide in the shared database.</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];
}
