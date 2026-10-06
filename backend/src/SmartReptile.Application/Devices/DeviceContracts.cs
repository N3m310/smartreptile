using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Devices;

/// <summary>
/// Self-registration input. Nullable members so a missing field becomes a field-level violation rather than a
/// deserialisation failure.
/// </summary>
public sealed record SelfRegisterRequest(string? ChipId, string? MacAddress, string? FirmwareVersion);

/// <summary>What the device needs in order to display a code (§07-appendices/03 §2.2).</summary>
/// <param name="DeviceId">The device's public id, e.g. <c>sr-3f9a2c</c> — not the surrogate key.</param>
/// <param name="ClaimCode">The code to show on the OLED, unformatted; display it with <c>ClaimCode.Format</c>.</param>
/// <param name="ExpiresAtUtc">When the code stops being accepted.</param>
/// <param name="AlreadyRegistered">
/// True when the board had registered before and this is a re-issued code. The API answers <c>409</c> in that
/// case rather than <c>201</c>, so a looping device is visible in the logs instead of looking like a fresh claim.
/// </param>
public sealed record SelfRegisterResult(
    string DeviceId,
    string ClaimCode,
    DateTimeOffset ExpiresAtUtc,
    bool AlreadyRegistered);

/// <summary>Claim input: the code off the screen plus the terrarium to bind to.</summary>
public sealed record ClaimRequest(string? ClaimCode, Guid TerrariumId);

/// <summary>
/// The one and only time the device secret is handed out (§02-design/06 §5).
/// </summary>
/// <param name="DeviceId">Public id of the claimed device.</param>
/// <param name="Secret">256-bit base32 secret. Never retrievable again — only its hash is stored.</param>
/// <param name="TerrariumId">Terrarium the device is now bound to.</param>
/// <param name="BoundAtUtc">Instant the binding was made.</param>
public sealed record ClaimResult(string DeviceId, string Secret, Guid TerrariumId, DateTimeOffset BoundAtUtc);

/// <summary>A rotated secret, plus the instant the previous one finally stops working.</summary>
/// <param name="DeviceId">Public id of the device.</param>
/// <param name="Secret">The replacement secret, returned once.</param>
/// <param name="PreviousUsableUntilUtc">End of the rotation grace window (BR-05.5).</param>
public sealed record RotatedSecret(string DeviceId, string Secret, DateTimeOffset PreviousUsableUntilUtc);

/// <summary>
/// An expected onboarding failure. Field-level violations reuse <see cref="IdentityViolation"/>: it already means
/// "a named field was refused, with a stable code", which is exactly what a bad chip id or MAC address is.
/// </summary>
/// <param name="Code">Stable problem code, e.g. <c>claim_code_invalid</c>.</param>
/// <param name="Message">Message safe to show the user — never reveals whether a code or id existed.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
/// <param name="DeviceId">Public id, present for <c>device_already_registered</c> so the client can show which board.</param>
public sealed record DeviceProblem(
    string Code,
    string Message,
    IReadOnlyList<IdentityViolation>? Errors = null,
    string? DeviceId = null);

/// <summary>
/// Outcome of an onboarding or credential use case. At most one payload is set; <see cref="Problem"/> being null
/// means success, and for <c>revoke</c> that is the whole result.
/// </summary>
public sealed record DeviceOutcome
{
    /// <summary>Set by self-register.</summary>
    public SelfRegisterResult? Registration { get; init; }

    /// <summary>Set by claim.</summary>
    public ClaimResult? Claim { get; init; }

    /// <summary>Set by rotate-secret.</summary>
    public RotatedSecret? Rotation { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public DeviceProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A self-registration answer (fresh or re-issued).</summary>
    public static DeviceOutcome Registered(SelfRegisterResult result) => new() { Registration = result };

    /// <summary>A successful claim.</summary>
    public static DeviceOutcome Claimed(ClaimResult result) => new() { Claim = result };

    /// <summary>A successful rotation.</summary>
    public static DeviceOutcome Rotated(RotatedSecret result) => new() { Rotation = result };

    /// <summary>A command that succeeded and has nothing to return.</summary>
    public static DeviceOutcome Done() => new();

    /// <summary>A failure with a code and a non-disclosing message.</summary>
    public static DeviceOutcome Failure(string code, string message, string? deviceId = null) =>
        new() { Problem = new DeviceProblem(code, message, null, deviceId) };

    /// <summary>A failure carrying field-level validation errors.</summary>
    public static DeviceOutcome Invalid(string code, string message, IReadOnlyList<IdentityViolation> errors) =>
        new() { Problem = new DeviceProblem(code, message, errors) };
}

/// <summary>Result of checking a device's credentials, as the MQTT broker and the HTTPS fallback need it.</summary>
/// <param name="IsValid">True when an unexpired, unrevoked credential matched.</param>
/// <param name="DeviceId">The device's surrogate key, when valid.</param>
/// <param name="TerrariumId">The bound terrarium, when valid and bound.</param>
public sealed record DeviceCredentialVerification(bool IsValid, Guid DeviceId, Guid? TerrariumId)
{
    /// <summary>A refusal. Deliberately one value: the caller learns nothing about which half was wrong.</summary>
    public static readonly DeviceCredentialVerification Rejected = new(false, Guid.Empty, null);
}
