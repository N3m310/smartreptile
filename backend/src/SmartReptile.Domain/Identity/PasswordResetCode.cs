namespace SmartReptile.Domain.Identity;

/// <summary>
/// A single-use password-reset code the server issues on request and delivers out of band
/// (§02-design/06 §2, BR-01.5).
/// </summary>
/// <remarks>
/// Deliberately a different thing from the backup code stored on <see cref="User"/>: that one is issued once at
/// registration and kept by the keeper, this one is generated whenever somebody asks and expires in minutes. The
/// <em>shape</em> of both is the same (<see cref="RecoveryCode"/>) so the flow a person performs is identical
/// whichever they hold, and nobody has to learn two kinds of code.
/// <para>
/// Only the digest is stored, and it is <b>unsalted on purpose</b>: the row has to be found before it can be
/// verified, exactly like a refresh token, and a per-row salt would turn that lookup into a table scan. A 99-bit
/// CSPRNG value does not need a salt to resist a precomputed table — the salt buys nothing here and costs the
/// index.
/// </para>
/// </remarks>
public class PasswordResetCode
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Account the code can reset.</summary>
    public Guid UserId { get; set; }

    /// <summary><c>SHA-256(code)</c>; the code itself exists only in the delivery channel and the request body.</summary>
    public byte[] CodeHash { get; set; } = Array.Empty<byte>();

    /// <summary>When the code was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When it stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when it is spent — or when a newer code replaces it.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Address the request came from, for abuse triage. Never used for authorisation.</summary>
    public string? RequestedFromAddress { get; set; }

    /// <summary>
    /// True when the code may still be spent: unexpired and never consumed. A consumed code reports false rather
    /// than being merely "unusable" — like a refresh token, presenting one again is evidence worth seeing, not an
    /// accident to tolerate silently.
    /// </summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) => ConsumedAt is null && ExpiresAt > nowUtc;
}
