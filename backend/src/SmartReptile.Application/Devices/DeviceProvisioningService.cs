using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Common;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using System.Text.Json;

namespace SmartReptile.Application.Devices;

/// <summary>
/// Task 2.2 — the device onboarding flow of UC-01: self-register, claim, rotate, revoke, and the credential check
/// the telemetry path uses.
/// </summary>
/// <remarks>
/// Three properties are deliberate and load-bearing:
/// <list type="bullet">
/// <item><b>Re-registration is idempotent.</b> A board that reboots before claiming gets a fresh code for the
/// same row rather than a second device, because <c>ChipId</c> is unique.</item>
/// <item><b>An unusable code is indistinguishable from an unknown one.</b> Unknown, expired and already-consumed
/// all return <c>claim_code_invalid</c>; the consumed case works because claiming clears the code, so the lookup
/// simply finds nothing (BR-04.3).</item>
/// <item><b>A foreign terrarium is a 404, not a 403.</b> "Not yours" and "does not exist" are the same answer, so
/// ids cannot be probed (BR-02.2).</item>
/// </list>
/// </remarks>
public sealed class DeviceProvisioningService(
    IProvisioningStore store,
    IClaimCodeGenerator claimCodes,
    IDeviceCredentials credentials,
    IOnboardingThrottleStore throttle,
    IDeviceSessionRegistry sessions,
    IClock clock,
    ProvisioningSettings settings)
{
    /// <summary>Attempts allowed before a new public id is treated as unallocatable rather than retried forever.</summary>
    private const int PublicIdAttempts = 5;

    /// <summary>
    /// Registers a board, or re-issues the code for one that registered but never claimed.
    /// Anonymous by necessity, so it is throttled per address and globally before anything is written.
    /// </summary>
    public async Task<DeviceOutcome> SelfRegisterAsync(
        SelfRegisterRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var limits = settings.Onboarding;

        var window = throttle.GetWindow(
            ipAddress,
            now.AddMinutes(-limits.IpWindowMinutes),
            now.AddMinutes(-limits.GlobalWindowMinutes));

        var decision = OnboardingThrottlePolicy.Evaluate(window.IpAttempts, window.GlobalAttempts, limits);

        if (decision.IsThrottled)
        {
            return DeviceOutcome.Failure(
                decision.Code!,
                "Too many registration attempts from this address. Try again in a few minutes.");
        }

        var violations = Validate(request);

        if (violations.Count > 0)
        {
            return DeviceOutcome.Invalid("registration_invalid", "The device details were refused.", violations);
        }

        // Every attempt is counted, including a repeat from the same board: a re-issued code is still a write.
        throttle.RecordAttempt(ipAddress, now);

        var chipId = request.ChipId!.Trim();
        var existing = await store.FindDeviceByChipIdAsync(chipId, cancellationToken);

        if (existing is not null)
        {
            if (DeviceProvisioningRules.IsClaimed(existing))
            {
                return DeviceOutcome.Failure(
                    "device_already_registered",
                    "This device is already registered and bound to a terrarium.",
                    existing.PublicId);
            }

            // A fresh code, not the old one: the previous code may have expired while the board was rebooting,
            // and re-issuing keeps a retry loop from stranding the device in provisioning mode (BR-04.1).
            var reissued = IssueClaimCode(existing, now);
            await store.SaveChangesAsync(cancellationToken);

            return DeviceOutcome.Registered(
                new SelfRegisterResult(existing.PublicId, reissued.Code, reissued.ExpiresAt, AlreadyRegistered: true));
        }

        var device = new Device
        {
            PublicId = await AllocatePublicIdAsync(cancellationToken),
            DeviceName = "New node",
            ChipId = chipId,
            MacAddress = request.MacAddress!.Trim(),
            FirmwareVersion = (request.FirmwareVersion ?? string.Empty).Trim(),
            Status = DeviceStatus.Provisioning,
            Protocol = DeviceProtocol.Mqtt,
        };

        var issued = IssueClaimCode(device, now);

        store.AddDevice(device);
        await store.SaveChangesAsync(cancellationToken);

        return DeviceOutcome.Registered(
            new SelfRegisterResult(device.PublicId, issued.Code, issued.ExpiresAt, AlreadyRegistered: false));
    }

    /// <summary>
    /// Binds a registered device to a terrarium and issues its secret. The secret is returned here and never again.
    /// </summary>
    /// <param name="request">Claim code plus the terrarium to bind to.</param>
    /// <param name="ownerUserId">The authenticated caller; the terrarium must belong to them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit row; omitted callers get <see cref="AuditActor.Unknown"/>.</param>
    public async Task<DeviceOutcome> ClaimAsync(
        ClaimRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null)
    {
        var now = clock.UtcNow;
        var code = ClaimCode.Normalise(request.ClaimCode);

        // A code of the wrong shape cannot match anything, and answering without a query keeps the reply time
        // from hinting at whether a row exists (BR-04.3).
        var device = ClaimCode.IsWellFormed(code, settings.ClaimCodeAlphabet, settings.ClaimCodeLength)
            ? await store.FindDeviceByClaimCodeAsync(code, cancellationToken)
            : null;

        // One answer for unknown, expired and already-consumed (BR-04.3).
        if (device is null || !DeviceProvisioningRules.ClaimCodeIsUsable(device, now))
        {
            return DeviceOutcome.Failure(
                "claim_code_invalid",
                "That claim code is not valid. Check the code shown on the device and try again.");
        }

        var terrarium = await store.FindOwnedTerrariumAsync(request.TerrariumId, ownerUserId, cancellationToken);

        if (terrarium is null)
        {
            return DeviceOutcome.Failure("not_found", "Terrarium not found.");
        }

        // Checked here for a clear message; the filtered unique index is the enforcement if two claims race.
        if (await store.TerrariumHasDeviceAsync(terrarium.Id, cancellationToken))
        {
            return DeviceOutcome.Failure("terrarium_already_bound", "That terrarium already has a device bound to it.");
        }

        var secret = credentials.NewSecret();
        var hashed = credentials.HashSecret(secret);

        store.AddCredential(new DeviceCredential
        {
            DeviceId = device.Id,
            SecretHash = hashed.Hash,
            Salt = hashed.Salt,
            IssuedAt = now,
        });

        device.TerrariumId = terrarium.Id;
        device.UserId = ownerUserId;
        device.ProvisionedAt = now;
        device.ClaimCode = null;
        device.ClaimCodeExpiresAt = null;
        device.Status = DeviceStatus.Provisioning;

        // One row for the whole request: the binding and the secret are the same action, so `device.bound` is not
        // written as well — two rows would make one claim look like two events.
        store.AddAuditEntry(AuditLog.ForDevice(
            AuditAction.DeviceClaimed,
            device.Id,
            device.PublicId,
            ownerUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: Serialize(new { terrariumId = terrarium.Id, userId = ownerUserId })));

        await store.SaveChangesAsync(cancellationToken);

        return DeviceOutcome.Claimed(new ClaimResult(device.PublicId, secret, terrarium.Id, now));
    }

    /// <summary>
    /// Issues a replacement secret and puts the previous one on a grace window, so the device can reconnect and
    /// persist the new one before the old stops working (BR-05.5).
    /// </summary>
    /// <param name="deviceId">Public id of the device.</param>
    /// <param name="ownerUserId">The authenticated caller, who must own the device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    public async Task<DeviceOutcome> RotateSecretAsync(
        string deviceId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null)
    {
        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || device.Status == DeviceStatus.Revoked || device.UserId != ownerUserId)
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        var graceUntil = now.AddMinutes(DeviceProvisioningRules.RotationGraceMinutes);

        foreach (var credential in device.Credentials.Where(credential => credential.IsUsableAt(now)))
        {
            credential.GraceUntil = graceUntil;
        }

        var secret = credentials.NewSecret();
        var hashed = credentials.HashSecret(secret);

        store.AddCredential(new DeviceCredential
        {
            DeviceId = device.Id,
            SecretHash = hashed.Hash,
            Salt = hashed.Salt,
            IssuedAt = now,
            IssuedByUserId = ownerUserId,
        });

        // The secret itself is never recorded — only that a rotation happened and how long the previous one stays
        // usable. The trail must not become a second place the credential lives.
        store.AddAuditEntry(AuditLog.ForDevice(
            AuditAction.DeviceSecretRotated,
            device.Id,
            device.PublicId,
            ownerUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: Serialize(new { issuedAt = now, previousUsableUntil = graceUntil })));

        await store.SaveChangesAsync(cancellationToken);

        return DeviceOutcome.Rotated(new RotatedSecret(device.PublicId, secret, graceUntil));
    }

    /// <summary>
    /// Revokes a device and every credential it holds. Idempotent: revoking twice is not an error.
    /// </summary>
    /// <remarks>
    /// The credential is what makes the revocation permanent, and closing the live session is what makes it
    /// immediate (BR-05.4: "disconnected within 60 s"). The kick happens after the write, so a session that
    /// survives it still cannot reconnect or publish. A repeat revoke changes nothing and therefore writes no
    /// audit row — the trail records what happened, not that someone asked twice.
    /// </remarks>
    /// <param name="deviceId">Public id of the device.</param>
    /// <param name="ownerUserId">The authenticated caller, who must own the device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    public async Task<DeviceOutcome> RevokeAsync(
        string deviceId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null)
    {
        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || device.UserId != ownerUserId)
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return DeviceOutcome.Done();
        }

        device.Status = DeviceStatus.Revoked;
        device.RevokedAt = now;
        device.ClaimCode = null;
        device.ClaimCodeExpiresAt = null;

        var credentialsRevoked = 0;

        foreach (var credential in device.Credentials.Where(credential => credential.RevokedAt is null))
        {
            credential.RevokedAt = now;
            credential.GraceUntil = null;
            credentialsRevoked++;
        }

        store.AddAuditEntry(AuditLog.ForDevice(
            AuditAction.DeviceRevoked,
            device.Id,
            device.PublicId,
            ownerUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: Serialize(new { status = nameof(DeviceStatus.Revoked), credentialsRevoked })));

        await store.SaveChangesAsync(cancellationToken);

        // After the write, so the credential check already refuses the device even if this kick fails or the
        // board reconnects before it takes effect.
        await sessions.KickAsync(device.PublicId, "device_revoked", cancellationToken);

        return DeviceOutcome.Done();
    }

    /// <summary>
    /// Checks a device's credentials, for the MQTT broker and the HTTPS fallback (FR-05). Honours the rotation
    /// grace window, so a device that reconnected with its previous secret during a rotation still works.
    /// </summary>
    public async Task<DeviceCredentialVerification> VerifyCredentialAsync(
        string? publicId,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(publicId) || string.IsNullOrEmpty(secret))
        {
            return DeviceCredentialVerification.Rejected;
        }

        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(publicId, cancellationToken);

        if (device is null || device.Status == DeviceStatus.Revoked)
        {
            return DeviceCredentialVerification.Rejected;
        }

        // Every usable credential is tried, not just the newest: during the grace window both are valid.
        foreach (var credential in device.Credentials.Where(credential => credential.IsUsableAt(now)))
        {
            if (credentials.VerifySecret(secret, credential.SecretHash, credential.Salt))
            {
                return new DeviceCredentialVerification(true, device.Id, device.TerrariumId);
            }
        }

        return DeviceCredentialVerification.Rejected;
    }

    /// <summary>
    /// Stamps a fresh code on the device and returns it. A re-issue replaces the previous code outright, so only
    /// one code is ever live for a board.
    /// </summary>
    private (string Code, DateTimeOffset ExpiresAt) IssueClaimCode(Device device, DateTimeOffset nowUtc)
    {
        var code = claimCodes.Generate();
        var expiresAt = DeviceProvisioningRules.ClaimCodeExpiry(nowUtc, settings.ClaimCodeMinutes);

        device.ClaimCode = code;
        device.ClaimCodeExpiresAt = expiresAt;

        return (code, expiresAt);
    }

    /// <summary>
    /// The snapshots an audit row carries. Only identifying and state-bearing fields go in, never a secret: the
    /// trail must not become a second place a credential lives (§02-design/02 §3.18).
    /// </summary>
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private async Task<string> AllocatePublicIdAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < PublicIdAttempts; attempt++)
        {
            var candidate = credentials.NewPublicId();

            if (!await store.PublicIdExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        // Reaching here means the generator is broken, not that the space ran out — the unique index would also
        // have caught it, but as an opaque 500 during a demo.
        throw new DomainValidationException(
            "public_id_exhausted",
            $"Could not allocate a unique device public id in {PublicIdAttempts} attempts.");
    }

    private static List<IdentityViolation> Validate(SelfRegisterRequest request)
    {
        var violations = new List<IdentityViolation>();

        var chipId = (request.ChipId ?? string.Empty).Trim();

        if (chipId.Length == 0)
        {
            violations.Add(new IdentityViolation("chipId", "chip_id_required", "A chip id is required."));
        }
        else if (chipId.Length > 32)
        {
            violations.Add(new IdentityViolation("chipId", "chip_id_too_long", "A chip id must be at most 32 characters."));
        }
        else if (!chipId.All(char.IsAsciiLetterOrDigit))
        {
            violations.Add(new IdentityViolation(
                "chipId", "chip_id_invalid", "A chip id may contain only letters and digits."));
        }

        var mac = (request.MacAddress ?? string.Empty).Trim();

        if (mac.Length == 0)
        {
            violations.Add(new IdentityViolation("macAddress", "mac_required", "A MAC address is required."));
        }
        else if (!IsMacAddress(mac))
        {
            violations.Add(new IdentityViolation(
                "macAddress", "mac_invalid", "A MAC address must look like AA:BB:CC:DD:EE:FF."));
        }

        var firmware = (request.FirmwareVersion ?? string.Empty).Trim();

        if (firmware.Length > 16)
        {
            violations.Add(new IdentityViolation(
                "firmwareVersion", "firmware_too_long", "A firmware version must be at most 16 characters."));
        }

        return violations;
    }

    private static bool IsMacAddress(string value)
    {
        if (value.Length != 17)
        {
            return false;
        }

        for (var index = 0; index < 17; index++)
        {
            var c = value[index];

            if (index % 3 == 2)
            {
                if (c != ':')
                {
                    return false;
                }
            }
            else if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
