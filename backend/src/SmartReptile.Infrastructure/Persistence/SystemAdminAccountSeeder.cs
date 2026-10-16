using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// Creates the one administrator account the species-profile catalogue's writes require, from the
/// <c>Admin</c> configuration section (§03-implementation/03 §2).
/// </summary>
/// <remarks>
/// <b>Why configuration and not registration.</b> <c>POST /auth/register</c> always produces an
/// <see cref="SystemRoles.Owner"/> — the role of the keeper who owns the terrariums — so the right to author the
/// shared reference data would otherwise be granted to every account that signs up. The administrator is therefore
/// seeded from deployment configuration, and a deployment that configures none simply has no account that can
/// author the catalogue: reads and the per-terrarium threshold overrides are unaffected.
/// <para>
/// <b>The configuration owns that credential.</b> A row this seeder created is re-checked on every start-up: if
/// the configured password no longer matches the stored hash, the hash is replaced. Rotating the administrator's
/// password is therefore an edit to <c>Admin:Password</c> plus a restart, which is also the only recovery path —
/// the seeded account carries no recovery code, because a start-up path that printed one would be a live
/// credential in a log.
/// </para>
/// <para>
/// <b>Nothing is written that the login path would refuse.</b> The username, the address and the password are put
/// through the same policies registration uses, and a violation is logged and skipped rather than inserted: an
/// administrator who cannot sign in is a worse outcome than no administrator, because it looks like one.
/// </para>
/// </remarks>
public sealed class SystemAdminAccountSeeder(
    SmartReptileDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<AdminAccountOptions> options,
    ILogger<SystemAdminAccountSeeder> logger)
{
    /// <summary>Seeds the configured administrator, unless the configuration names none.</summary>
    /// <returns><c>true</c> when a row was created; <c>false</c> when the step was a no-op for any reason.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            if (!string.IsNullOrWhiteSpace(settings.Password))
            {
                logger.LogError(
                    "Admin:Password is set but Admin:Username is empty; no administrator was created and the "
                    + "species-profile writes stay unavailable");
            }
            else
            {
                logger.LogInformation(
                    "No system administrator is configured (Admin:Username is empty); none was created");
            }

            return false;
        }

        var username = AccountIdentifierPolicy.NormaliseUsername(settings.Username);
        var email = AccountIdentifierPolicy.NormaliseEmail(settings.Email);

        var violations = new List<IdentityViolation>();
        violations.AddRange(AccountIdentifierPolicy.ValidateUsername(settings.Username));
        violations.AddRange(AccountIdentifierPolicy.ValidateEmail(settings.Email));
        violations.AddRange(PasswordPolicy.Validate(settings.Password));

        if (violations.Count > 0)
        {
            logger.LogError(
                "The configured system administrator Admin:Username={Username} was refused ({Violations}); no "
                + "account was created. A missing email address or a password the policy rejects is a typo in "
                + "configuration, not a runtime fault.",
                settings.Username,
                string.Join(", ", violations.Select(violation => violation.Code)));

            return false;
        }

        var existing = await db.Users
            .FirstOrDefaultAsync(user => user.Username == username || user.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = passwordHasher.Hash(settings.Password);

            // The role is what this step exists for; the timestamps and the defaults come from the entity. No
            // recovery code is issued — see the remarks on the class.
            db.Users.Add(new User
            {
                Username = username,
                Email = email,
                PasswordHash = created.Hash,
                PasswordSalt = created.Salt,
                PasswordIterations = created.Iterations,
                RoleId = SystemRoles.AdminId,
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Created the system administrator {Username} (role Admin)", username);

            return true;
        }

        // An Admin row can only have come from here or from an operator's deliberate UPDATE, and both mean the
        // same thing: the configuration owns the credential. Any other role is an account that merely shares the
        // name, and it is left exactly as its owner set it up.
        if (existing.RoleId != SystemRoles.AdminId)
        {
            logger.LogWarning(
                "Admin:Username={Username} (Admin:Email={Email}) names an account that already exists ({Existing}, "
                + "role {Role}); nothing was written. Choose a free identifier in Admin:Username/Admin:Email, or "
                + "promote that account to Admin deliberately.",
                username,
                email,
                existing.Username,
                existing.Role);

            return false;
        }

        var stored = new PasswordHash(existing.PasswordHash, existing.PasswordSalt, existing.PasswordIterations);

        if (passwordHasher.Verify(settings.Password, stored))
        {
            logger.LogInformation(
                "The system administrator {Username} already exists; its configured credential is unchanged",
                username);

            return false;
        }

        var rotated = passwordHasher.Hash(settings.Password);
        existing.PasswordHash = rotated.Hash;
        existing.PasswordSalt = rotated.Salt;
        existing.PasswordIterations = rotated.Iterations;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "The system administrator {Username} had its password replaced from Admin:Password", username);

        return false;
    }
}
