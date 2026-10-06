namespace SmartReptile.Domain.Identity;

/// <summary>
/// One account-rule problem, mapped to a field-level error in the RFC 7807 response. Modelled as a value
/// rather than an exception for the same reason as <see cref="Thresholds.BandViolation"/>: these are expected
/// user-input failures, not invariant violations (§03-implementation/02 §2.3).
/// </summary>
/// <param name="Field">Field name, camelCase, e.g. <c>password</c> or <c>email</c>.</param>
/// <param name="Code">Stable problem code, e.g. <c>password_too_short</c>.</param>
/// <param name="Message">Human-readable explanation; the client localises its own text from the code.</param>
public sealed record IdentityViolation(string Field, string Code, string Message);
