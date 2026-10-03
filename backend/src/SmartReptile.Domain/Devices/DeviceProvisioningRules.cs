namespace SmartReptile.Domain.Devices;

/// <summary>
/// The state rules of the onboarding flow (UC-01, §02-design/06 §5). Kept pure and separate from the service that
/// applies them, so the "may this code be consumed?" question has exactly one answer and one test.
/// </summary>
public static class DeviceProvisioningRules
{
    /// <summary>Claim-code lifetime (BR-04.1).</summary>
    public const int ClaimCodeTtlMinutes = 15;

    /// <summary>
    /// How long the previous secret stays valid after a rotation, so the device can reconnect and persist the
    /// replacement (BR-05.5).
    /// </summary>
    public const int RotationGraceMinutes = 10;

    /// <summary>
    /// True when the device is bound to a terrarium. A bound device is never re-claimable, and its claim code is
    /// cleared at claim time so a second attempt simply does not match anything (BR-04.3).
    /// </summary>
    public static bool IsClaimed(Device device) => device.TerrariumId is not null;

    /// <summary>
    /// True when the device's current claim code may be consumed: one exists, it has not expired, the device is
    /// still unclaimed and it has not been revoked.
    /// </summary>
    public static bool ClaimCodeIsUsable(Device device, DateTimeOffset nowUtc) =>
        !string.IsNullOrEmpty(device.ClaimCode)
        && device.ClaimCodeExpiresAt is { } expiry
        && expiry > nowUtc
        && !IsClaimed(device)
        && device.Status != DeviceStatus.Revoked;

    /// <summary>Expiry stamped on a freshly issued (or re-issued) claim code.</summary>
    public static DateTimeOffset ClaimCodeExpiry(DateTimeOffset nowUtc, int ttlMinutes) =>
        nowUtc.AddMinutes(ttlMinutes);
}
