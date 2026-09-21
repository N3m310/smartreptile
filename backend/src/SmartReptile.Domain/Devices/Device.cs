using SmartReptile.Domain.Readings;

namespace SmartReptile.Domain.Devices;

/// <summary>Device lifecycle states (§02-design/02 §4.1).</summary>
public enum DeviceStatus
{
    Provisioning = 0,
    Online = 1,
    Offline = 2,
    Revoked = 3,
    Maintenance = 4,
}

/// <summary>How the device talks to the backend.</summary>
public enum DeviceProtocol
{
    Mqtt = 0,
    HttpFallback = 1,
}

/// <summary>
/// The ESP32 sensor node (FR-04, FR-05, FR-16). Monitoring-only: it reports values, faults and health, and
/// holds no thresholds (ADR-005, ADR-008).
/// </summary>
public class Device
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human/device-facing short id, e.g. <c>sr-3f9a2c</c>. Used in MQTT topics.</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Display name, renameable by the owner.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>ESP32 chip id, unique per board; makes re-registration idempotent.</summary>
    public string ChipId { get; set; } = string.Empty;

    /// <summary>Wi-Fi MAC address reported by the device.</summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Firmware version reported in the health payload.</summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>Current lifecycle state.</summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Provisioning;

    /// <summary>Transport currently in use.</summary>
    public DeviceProtocol Protocol { get; set; } = DeviceProtocol.Mqtt;

    /// <summary>Bound terrarium; null while unclaimed (DI-04 enforces at most one device per terrarium).</summary>
    public Guid? TerrariumId { get; set; }

    /// <summary>Bound terrarium navigation.</summary>
    public Terrariums.Terrarium? Terrarium { get; set; }

    /// <summary>Owner at claim time.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Short-lived, single-use onboarding code shown on the OLED (BR-04.1). Stored plaintext on purpose —
    /// it is not a credential, expires in minutes and is only valid while the device is unclaimed (§02-design/06 §5).
    /// </summary>
    public string? ClaimCode { get; set; }

    /// <summary>When the current claim code stops being accepted.</summary>
    public DateTimeOffset? ClaimCodeExpiresAt { get; set; }

    /// <summary>When the device was successfully bound to a terrarium.</summary>
    public DateTimeOffset? ProvisionedAt { get; set; }

    /// <summary>When the credential was revoked; a revoked device cannot publish (BR-05.4).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Last sample or health message received — drives the online/offline indicator.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Desired sampling interval pushed downlink (default 60 s, allowed 10–300 s).</summary>
    public int SamplingIntervalSec { get; set; } = 60;

    /// <summary>Desired publish interval pushed downlink.</summary>
    public int PublishIntervalSec { get; set; } = 60;

    /// <summary>Per-device calibration offsets applied at ingest (BR-06.7/BR-07.4), e.g. <c>{"tempOffsetC":-0.4}</c>.</summary>
    public string? CalibrationJson { get; set; }

    /// <summary>Last reported Wi-Fi signal strength, denormalised for the fleet list (FR-16).</summary>
    public int? SignalStrengthDbm { get; set; }

    /// <summary>Last reported battery percentage, if the node is battery powered.</summary>
    public decimal? BatteryPct { get; set; }

    /// <summary>Last reported uptime in seconds.</summary>
    public long? UptimeSeconds { get; set; }

    /// <summary>Last reported free heap in kilobytes — early warning for leaks (M5 soak).</summary>
    public int? FreeHeapKb { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Rotation/revocation history for the device secrets.</summary>
    public ICollection<DeviceCredential> Credentials { get; set; } = new List<DeviceCredential>();
}

/// <summary>
/// A device credential: 256-bit secret, stored only as a hash (§02-design/06 §4.1, ADR-006).
/// Rotation issues a new row and leaves the old one valid until <see cref="GraceUntil"/>.
/// </summary>
public class DeviceCredential
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning device.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Owning device navigation.</summary>
    public Device? Device { get; set; }

    /// <summary><c>SHA-256(secret ‖ salt)</c>; the plaintext secret exists only in the device and in the claim response.</summary>
    public byte[] SecretHash { get; set; } = Array.Empty<byte>();

    /// <summary>Per-credential salt.</summary>
    public byte[] Salt { get; set; } = Array.Empty<byte>();

    /// <summary>When the credential was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional hard expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Set when the credential stops being accepted.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Rotation grace window: the previous credential stays valid until this instant (BR-05.5).</summary>
    public DateTimeOffset? GraceUntil { get; set; }

    /// <summary>User who triggered the rotation; null for the credential created during self-registration.</summary>
    public Guid? IssuedByUserId { get; set; }

    /// <summary>True when the credential may be used at <paramref name="nowUtc"/>.</summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) =>
        RevokedAt is null
        && (ExpiresAt is null || ExpiresAt > nowUtc)
        && (GraceUntil is null || GraceUntil > nowUtc);
}

/// <summary>One health report from a device (FR-16 fleet view, NFR-12 observability).</summary>
public class DeviceHealthSample
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Device that produced the report.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Terrarium at the time of reporting (copied for history integrity).</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Device timestamp (UTC).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Wi-Fi signal strength in dBm.</summary>
    public int? RssiDbm { get; set; }

    /// <summary>Device uptime in seconds.</summary>
    public long? UptimeSeconds { get; set; }

    /// <summary>Free heap in kilobytes.</summary>
    public int? FreeHeapKb { get; set; }

    /// <summary>Battery percentage, if applicable.</summary>
    public decimal? BatteryPct { get; set; }

    /// <summary>Firmware version at report time.</summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>Quality bitmask carried from the firmware.</summary>
    public QualityFlags QualityFlags { get; set; }
}
