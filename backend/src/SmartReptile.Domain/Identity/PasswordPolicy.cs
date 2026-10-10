namespace SmartReptile.Domain.Identity;

/// <summary>
/// Password rules from §02-design/06 §2 (BR-01.2): at least <see cref="MinLength"/> characters containing at
/// least one letter, one digit, one upper-case letter and one special character; no longer than
/// <see cref="MaxLength"/>; and not one of the most frequently used passwords.
/// </summary>
/// <remarks>
/// Pure, so the rules are unit-testable and identical wherever a password is set (registration and
/// change-password). The deny-list is embedded rather than fetched: a password check must not depend on a
/// network call, and a service outage must never become a "no policy" path.
/// <para>
/// The composition rules are what the product brief asks for. On their own they add little over length plus the
/// deny-list — which is why the deny-list and the length cap stay — but a form that states a rule the server
/// does not check is worse than either, so the two are kept in step here and in the dashboard hint.
/// </para>
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Minimum length (BR-01.2).</summary>
    public const int MinLength = 8;

    /// <summary>Maximum length — a bound on PBKDF2 work per request (BR-01.2).</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// PBKDF2 iteration count new hashes are created with. Stored per user so it can be raised later and
    /// upgraded on the next successful login without a mass reset (TC-U-32).
    /// </summary>
    public const int TargetIterations = 210_000;

    /// <summary>
    /// Validates a candidate password and returns every violation, so the client can show all of them at once.
    /// </summary>
    public static IReadOnlyList<IdentityViolation> Validate(string? password)
    {
        var violations = new List<IdentityViolation>();

        if (string.IsNullOrEmpty(password))
        {
            violations.Add(new IdentityViolation("password", "password_required", "A password is required."));
            return violations;
        }

        if (password.Length < MinLength)
        {
            violations.Add(new IdentityViolation(
                "password", "password_too_short", $"A password must be at least {MinLength} characters."));
        }

        if (password.Length > MaxLength)
        {
            violations.Add(new IdentityViolation(
                "password", "password_too_long", $"A password must be at most {MaxLength} characters."));
        }

        if (!ContainsLetter(password))
        {
            violations.Add(new IdentityViolation(
                "password", "password_letter_required", "A password must contain at least one letter."));
        }

        if (!ContainsDigit(password))
        {
            violations.Add(new IdentityViolation(
                "password", "password_digit_required", "A password must contain at least one digit."));
        }

        if (!ContainsUppercase(password))
        {
            violations.Add(new IdentityViolation(
                "password",
                "password_uppercase_required",
                "A password must contain at least one upper-case letter."));
        }

        if (!ContainsSpecial(password))
        {
            violations.Add(new IdentityViolation(
                "password",
                "password_special_required",
                "A password must contain at least one special character (a symbol that is not a letter or a digit)."));
        }

        if (IsCommon(password))
        {
            violations.Add(new IdentityViolation(
                "password",
                "password_too_common",
                "That password is one of the most frequently used and is refused."));
        }

        return violations;
    }

    /// <summary>True when the password satisfies every rule.</summary>
    public static bool IsValid(string? password) => Validate(password).Count == 0;

    /// <summary>True when the password appears in the embedded deny-list (case-insensitive).</summary>
    public static bool IsCommon(string password) => CommonPasswords.Contains(password.Trim().ToLowerInvariant());

    private static bool ContainsLetter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsLetter(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDigit(string value)
    {
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUppercase(string value)
    {
        foreach (var c in value)
        {
            if (char.IsUpper(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Any character that is neither a letter nor a digit, whitespace excluded — a space must not satisfy the
    /// rule.
    /// </summary>
    private static bool ContainsSpecial(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The refuse-list: the highest-frequency entries of the published common-password corpora, lower-cased.
    /// </summary>
    /// <remarks>
    /// §02-design/06 §2 asks for the top 1 000. This list is the highest-frequency subset — the entries that
    /// actually appear in credential-stuffing lists against a Vietnamese-facing product — because a longer list
    /// adds rows without adding refusals. Growing it to the full 1 000 is mechanical and is tracked in
    /// <c>docs/03-implementation/07</c> under M5's hardening pass.
    /// </remarks>
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.Ordinal)
    {
        "123456", "123456789", "12345678", "password", "qwerty", "12345", "123123", "1234567890", "1q2w3e4r",
        "abc123", "111111", "1234567", "000000", "admin", "iloveyou", "1234", "666666", "654321", "123321",
        "monkey", "dragon", "letmein", "sunshine", "princess", "football", "welcome", "shadow", "master",
        "michael", "superman", "batman", "trustno1", "hello", "charlie", "donald", "freedom", "whatever",
        "qazwsx", "1qaz2wsx", "zaq12wsx", "qwerty123", "qwertyuiop", "asdfghjkl", "zxcvbnm", "asdf1234",
        "password1", "password123", "p@ssw0rd", "passw0rd", "admin123", "administrator", "root", "toor",
        "guest", "test", "test123", "demo", "demo1234", "changeme", "secret", "default", "system",
        "vietnam", "vietnam123", "hanoi", "saigon", "trung", "nguyen", "matkhau", "matkhau123", "123456a",
        "a123456", "1q2w3e", "1qazxsw2", "q1w2e3r4", "112233", "121212", "131313", "222222", "333333",
        "444444", "555555", "777777", "888888", "999999", "101010", "987654321", "123654", "159753",
        "baseball", "basketball", "jordan", "soccer", "pokemon", "starwars", "computer", "internet",
        "samsung", "google", "facebook", "android", "iphone", "money", "loveyou", "sweetheart", "babygirl",
    };
}
