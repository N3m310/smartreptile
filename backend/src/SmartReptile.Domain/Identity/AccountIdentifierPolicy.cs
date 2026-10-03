namespace SmartReptile.Domain.Identity;

/// <summary>
/// Rules for the account identifiers entered at registration. Usernames are stored lower-case so that
/// "Linh" and "linh" cannot become two accounts; the unique indexes on <c>Username</c> and <c>Email</c> are
/// the enforcement of record, and these checks exist so a caller gets a field-level error instead of a
/// database exception.
/// </summary>
public static class AccountIdentifierPolicy
{
    /// <summary>Minimum username length.</summary>
    public const int UsernameMinLength = 3;

    /// <summary>Matches the <c>Username</c> column length (§07-appendices/02).</summary>
    public const int UsernameMaxLength = 32;

    /// <summary>Matches the <c>Email</c> column length.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>Normalises a username to its stored form.</summary>
    public static string NormaliseUsername(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Normalises an email to its stored form. Case is not significant in the local part here.</summary>
    public static string NormaliseEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Validates a username, returning every violation.</summary>
    public static IReadOnlyList<IdentityViolation> ValidateUsername(string? username)
    {
        var violations = new List<IdentityViolation>();
        var value = NormaliseUsername(username);

        if (value.Length == 0)
        {
            violations.Add(new IdentityViolation("username", "username_required", "A username is required."));
            return violations;
        }

        if (value.Length < UsernameMinLength)
        {
            violations.Add(new IdentityViolation(
                "username", "username_too_short", $"A username must be at least {UsernameMinLength} characters."));
        }

        if (value.Length > UsernameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "username", "username_too_long", $"A username must be at most {UsernameMaxLength} characters."));
        }

        if (!char.IsAsciiLetter(value[0]))
        {
            violations.Add(new IdentityViolation(
                "username", "username_invalid", "A username must start with a letter."));
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                violations.Add(new IdentityViolation(
                    "username",
                    "username_invalid",
                    "A username may contain only letters, digits, '.', '_' and '-'."));
                break;
            }
        }

        return violations;
    }

    /// <summary>
    /// Validates an email address structurally. Deliberately shallow: without email infrastructure there is
    /// nothing to confirm against, and a stricter pattern would reject valid addresses while stopping nothing.
    /// </summary>
    public static IReadOnlyList<IdentityViolation> ValidateEmail(string? email)
    {
        var violations = new List<IdentityViolation>();
        var value = NormaliseEmail(email);

        if (value.Length == 0)
        {
            violations.Add(new IdentityViolation("email", "email_required", "An email address is required."));
            return violations;
        }

        if (value.Length > EmailMaxLength)
        {
            violations.Add(new IdentityViolation(
                "email", "email_too_long", $"An email address must be at most {EmailMaxLength} characters."));
            return violations;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        var valid = at > 0
                    && at == value.LastIndexOf('@')
                    && at < value.Length - 1
                    && value.IndexOf('.', at) > at + 1
                    && !value.Contains(' ', StringComparison.Ordinal)
                    && value[at + 1] != '.'
                    && !value.EndsWith('.');

        if (!valid)
        {
            violations.Add(new IdentityViolation("email", "email_invalid", "That is not a valid email address."));
        }

        return violations;
    }
}
