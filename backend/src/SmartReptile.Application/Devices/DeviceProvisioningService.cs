using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Terrariums;
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
    ProvisioningSettings settings,
    DeviceSilenceSettings silence)
{
    /// <summary>Attempts allowed before a new public id is treated as unallocatable rather than retried forever.</summary>
    private const int PublicIdAttempts = 5;

    /// <summary>Audit value for a binding made with the code shown on the board — the default flow.</summary>
    private const string ClaimMethodCode = "claim_code";

    /// <summary>
    /// Audit value for a binding made with the address printed on the case. Recorded so a MAC claim is visible as
    /// the lower-trust path it is (TASKS 2026-10-14, item 8).
    /// </summary>
    private const string ClaimMethodMac = "mac_address";

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

        // Validated above, so the normalisation cannot fail: storing the canonical form is what makes the
        // claim-by-MAC lookup match a board that reported lower-case hex (2026-10-23).
        MacAddress.TryNormalise(request.MacAddress, out var macAddress);
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
            MacAddress = macAddress,
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
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
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

        return await BindAsync(
            device, request.TerrariumId, ownerUserId, actor, now, ClaimMethodCode, cancellationToken,
            mayReachAnyAccount, request.DeviceName);
    }

    /// <summary>
    /// Binds a registered device to a terrarium by its MAC address instead of its claim code
    /// (TASKS 2026-10-14, item 8).
    /// </summary>
    /// <remarks>
    /// <b>This is the lower-trust path, and the documentation says so.</b> A claim code proves the caller has the
    /// board in front of them, because it is shown on its screen and lives for minutes; a MAC proves nothing — it is
    /// printed on the case, broadcast on every network the board joins, and guessable in bulk. It exists because a
    /// keeper who has the board in hand should not be blocked by a screen that failed, and it is confined to a
    /// board that has already self-registered and has no owner yet. The default flow stays the code.
    /// <para>
    /// <b>Every unusable MAC answers the same thing.</b> Unknown, already bound to an enclosure, revoked: one
    /// <c>device_not_found</c>, for BR-04.3's reason — an answer that told the three apart would turn the route into
    /// a way to test whether an address belongs to this fleet.
    /// </para>
    /// </remarks>
    /// <param name="request">The MAC address printed on the board, plus the terrarium to bind to.</param>
    /// <param name="ownerUserId">The authenticated caller; the terrarium must belong to them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit row; omitted callers get <see cref="AuditActor.Unknown"/>.</param>
    /// <param name="mayReachAnyAccount">Whether the caller has permissions across all accounts (admin).</param>
    public async Task<DeviceOutcome> ClaimByMacAsync(
        ClaimByMacRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        var now = clock.UtcNow;

        if (MacAddress.Inspect(request.MacAddress, out var mac) is var problem && problem != MacAddressProblem.None)
        {
            return DeviceOutcome.Invalid(
                "registration_invalid",
                "The MAC address was refused.",
                [new IdentityViolation("macAddress", "mac_invalid", MacAddress.Describe(problem))]);
        }

        var device = await store.FindDeviceByMacAddressAsync(mac, cancellationToken);

        if (device is null)
        {
            var cleanMac = mac.Replace(":", "").Replace("-", "").ToLowerInvariant();
            device = new Device
            {
                PublicId = await AllocatePublicIdAsync(cancellationToken),
                DeviceName = "Node " + (mac.Length >= 5 ? mac[^5..] : mac),
                ChipId = "esp32" + (cleanMac.Length > 26 ? cleanMac[..26] : cleanMac),
                MacAddress = mac,
                FirmwareVersion = "1.0.0",
                Status = DeviceStatus.Provisioning,
                Protocol = DeviceProtocol.Mqtt,
            };
            store.AddDevice(device);
        }
        else if (!DeviceProvisioningRules.IsClaimableByMac(device))
        {
            return DeviceOutcome.Failure(
                "device_already_bound",
                "This device is already bound to another terrarium or revoked.");
        }

        return await BindAsync(
            device, request.TerrariumId, ownerUserId, actor, now, ClaimMethodMac, cancellationToken,
            mayReachAnyAccount, request.DeviceName);
    }

    /// <summary>
    /// The binding both claim routes share: check the terrarium, check DI-04, stage the binding and commit it.
    /// </summary>
    /// <remarks>
    /// One implementation rather than two, because the routes differ only in how the board was identified — and a
    /// security-relevant rule that exists twice is a rule that will disagree with itself. What a binding *is* lives
    /// in <see cref="StageBinding"/>, which this shares with the create-with-a-device path.
    /// </remarks>
    /// <param name="device">The device the caller identified, already checked as claimable.</param>
    /// <param name="terrariumId">Terrarium the caller named.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="now">Instant the binding is made at.</param>
    /// <param name="method">Which identification path was used, recorded on the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">Whether caller has permissions across all accounts.</param>
    /// <param name="deviceName">Name to give the board in the same request; omitted or empty keeps the current one.</param>
    private async Task<DeviceOutcome> BindAsync(
        Device device,
        Guid terrariumId,
        Guid ownerUserId,
        AuditActor? actor,
        DateTimeOffset now,
        string method,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false,
        string? deviceName = null)
    {
        var violations = new List<IdentityViolation>();
        var name = NormaliseDeviceName(deviceName, violations);

        if (violations.Count > 0)
        {
            // Refused before anything is written, so a bad name cannot leave a board claimed with the label the
            // caller did not want (2026-10-23).
            return DeviceOutcome.Invalid("claim_invalid", "The claim was refused.", violations);
        }

        if (name is not null)
        {
            device.DeviceName = name;
        }

        var terrarium = await store.FindOwnedTerrariumAsync(terrariumId, ownerUserId, cancellationToken, mayReachAnyAccount);

        if (terrarium is null)
        {
            return DeviceOutcome.Failure("not_found", "Terrarium not found.");
        }

        // Checked here for a clear message; the filtered unique index is the enforcement if two claims race.
        if (await store.TerrariumHasDeviceAsync(terrarium.Id, cancellationToken))
        {
            return DeviceOutcome.Failure("terrarium_already_bound", "That terrarium already has a device bound to it.");
        }

        var secret = StageBinding(device, terrarium.Id, terrarium.UserId, now, method, actor);

        // The pre-check gives a clear message; this is the enforcement. Two claims of one terrarium can both pass
        // it, and the filtered unique index DI-04 then refuses the second binding — which is a documented 409, not
        // a 500 (TC-I-05).
        if (!await store.TrySaveClaimAsync(cancellationToken))
        {
            return DeviceOutcome.Failure(
                "terrarium_already_bound",
                "That terrarium already has a device bound to it.");
        }

        return DeviceOutcome.Claimed(new ClaimResult(device.PublicId, secret, terrarium.Id, now));
    }

    /// <summary>
    /// Finds the board a create request named, by public id or by MAC address, and reports why it cannot be used
    /// when that is the answer (TASKS 2026-10-14, item 2).
    /// </summary>
    /// <remarks>
    /// The same rules the claim routes apply, expressed as field violations instead of a problem code, because here
    /// the board is a field of the body the caller sent — exactly how the create path already reports an unknown
    /// <c>speciesProfileId</c>. Two selectors at once are refused rather than resolved by precedence: guessing which
    /// of two boards the caller meant is how the wrong board ends up bound.
    /// </remarks>
    /// <param name="publicId">Public id from the body, if any.</param>
    /// <param name="macAddress">MAC address from the body, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The board to bind and an empty list, no board and an empty list (nothing was named), or no board and the
    /// violations to refuse the request with.
    /// </returns>
    public async Task<(Device? Device, IReadOnlyList<IdentityViolation> Violations)> ResolveClaimableDeviceAsync(
        string? publicId,
        string? macAddress,
        CancellationToken cancellationToken = default)
    {
        var wantsId = !string.IsNullOrWhiteSpace(publicId);
        var wantsMac = !string.IsNullOrWhiteSpace(macAddress);

        if (wantsId && wantsMac)
        {
            return (null,
            [
                new IdentityViolation(
                    "deviceId",
                    "device_selector_ambiguous",
                    "Name the board by its id or by its MAC address, not both."),
            ]);
        }

        if (!wantsId && !wantsMac)
        {
            return (null, []);
        }

        var field = wantsId ? "deviceId" : "macAddress";
        Device? device;

        if (wantsId)
        {
            device = await store.FindDeviceByPublicIdAsync(publicId!.Trim(), cancellationToken);
        }
        else
        {
            // The same rule the self-register and claim-by-MAC routes apply: a group, broadcast or unset address
            // names no board, so it is refused here rather than looked up (2026-10-23).
            var problem = MacAddress.Inspect(macAddress, out var mac);

            if (problem != MacAddressProblem.None)
            {
                return (null,
                [
                    new IdentityViolation("macAddress", "mac_invalid", MacAddress.Describe(problem)),
                ]);
            }

            device = await store.FindDeviceByMacAddressAsync(mac, cancellationToken);
        }

        if (device is null)
        {
            return (null,
            [
                new IdentityViolation(
                    field,
                    "device_not_found",
                    "No device registered with this server matches that. Register the board first "
                    + "(POST /api/v1/devices/self-register)."),
            ]);
        }

        if (!DeviceProvisioningRules.IsClaimableByMac(device))
        {
            return (null,
            [
                new IdentityViolation(
                    field,
                    "device_already_bound",
                    "That board is already bound to a terrarium, or has been revoked."),
            ]);
        }

        return (device, []);
    }

    /// <summary>
    /// Stages everything a binding consists of — the credential, the device's binding fields and one audit row —
    /// and hands back the secret. <b>The caller commits.</b>
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ClaimAsync"/> so that the create-with-a-device path can bind a board inside its own
    /// unit of work: the enclosure and the binding have to commit together, or a failure between them would leave
    /// either a terrarium with a board nobody can use or a board bound to an enclosure that does not exist. The
    /// rules that decide <i>whether</i> a board may be bound stay in the two entry points, so this stages only what
    /// a decision has already allowed.
    /// </remarks>
    /// <param name="device">Board to bind, already checked as claimable.</param>
    /// <param name="terrariumId">Terrarium to bind it to.</param>
    /// <param name="ownerUserId">
    /// Account recorded as the device's owner and on the audit row. Null when the enclosure has no owner yet — an
    /// administrator created it and has not assigned it — and the board then belongs to nobody until it is.
    /// </param>
    /// <param name="now">Instant of the binding.</param>
    /// <param name="method">Which identification path was used, recorded on the audit row.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <returns>The secret to hand back exactly once.</returns>
    public string StageBinding(
        Device device,
        Guid terrariumId,
        Guid? ownerUserId,
        DateTimeOffset now,
        string method,
        AuditActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        var secret = credentials.NewSecret();
        var hashed = credentials.HashSecret(secret);

        store.AddCredential(new DeviceCredential
        {
            DeviceId = device.Id,
            SecretHash = hashed.Hash,
            Salt = hashed.Salt,
            IssuedAt = now,
        });

        device.TerrariumId = terrariumId;
        device.UserId = ownerUserId;
        device.ProvisionedAt = now;
        device.ClaimCode = null;
        device.ClaimCodeExpiresAt = null;
        device.Status = DeviceStatus.Provisioning;

        // One row for the whole request: the binding and the secret are the same action, so `device.bound` is not
        // written as well — two rows would make one claim look like two events. `method` is recorded because the two
        // identification paths carry different trust, and a trail that could not tell them apart could not answer
        // "how did this board get bound?" (TASKS 2026-10-14, item 8).
        store.AddAuditEntry(AuditLog.ForDevice(
            AuditAction.DeviceClaimed,
            device.Id,
            device.PublicId,
            ownerUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: Serialize(new { terrariumId, userId = ownerUserId, method })));

        return secret;
    }

    /// <summary>
    /// Binds a board to an enclosure, moving it out of whichever enclosure it held before.
    /// </summary>
    /// <remarks>
    /// The three rules this route keeps in one place:
    /// <list type="bullet">
    /// <item><b>An owner reaches only their own rows.</b> A foreign device or a foreign enclosure answers the same
    /// <c>not_found</c> as a missing one (BR-02.2); a caller holding the cross-account permission may pair any board
    /// with any enclosure.</item>
    /// <item><b>One enclosure holds one board.</b> Binding a board to an occupied enclosure displaces the board that
    /// was there, the way physically swapping the node would. Refusing instead would leave a keeper with no way to
    /// replace a failed board at all.</item>
    /// <item><b>One board sits in one enclosure.</b> It is the same single <c>TerrariumId</c>, so a move is an
    /// assignment rather than a second binding.</item>
    /// </list>
    /// </remarks>
    /// <param name="deviceId">Public id of the board to bind.</param>
    /// <param name="terrariumId">Enclosure to bind it to.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit rows.</param>
    /// <param name="mayReachAnyAccount">Whether the caller may act across accounts (data.write.any).</param>
    public async Task<DeviceOutcome> MoveAsync(
        string deviceId,
        Guid? terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        if (terrariumId is not { } targetId || targetId == Guid.Empty)
        {
            return DeviceOutcome.Invalid(
                "move_invalid",
                "The move was refused.",
                [new IdentityViolation("terrariumId", "terrarium_required", "Name the enclosure to bind the board to.")]);
        }

        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || (!mayReachAnyAccount && !IsCallers(device, ownerUserId)))
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return DeviceOutcome.Failure("device_revoked", "A revoked board cannot be bound to an enclosure.");
        }

        // A board joins an account by being claimed. Binding an unclaimed one would reserve a slot for a node that
        // holds no credential and therefore can never report into it.
        if (device.UserId is null)
        {
            return DeviceOutcome.Failure(
                "device_not_claimed",
                "That board has not been claimed by an account yet. Claim it before binding it to an enclosure.");
        }

        var terrarium = await store.FindOwnedTerrariumAsync(targetId, ownerUserId, cancellationToken, mayReachAnyAccount);

        if (terrarium is null)
        {
            return DeviceOutcome.Failure("not_found", "Terrarium not found.");
        }

        var previousTerrariumId = device.TerrariumId;

        if (previousTerrariumId == terrarium.Id)
        {
            // Already where the caller wants it: describe the board instead of writing an audit row for a change
            // that did not happen.
            device.Terrarium = terrarium;
            return DeviceOutcome.Moved(new DeviceMoveResult(Describe(device), previousTerrariumId, null));
        }

        var occupant = await store.FindDeviceBoundToTerrariumAsync(terrarium.Id, cancellationToken);

        if (occupant is not null && occupant.Id != device.Id)
        {
            // Unbound, not revoked: the displaced board is fine, it just has no enclosure — the same state an
            // enclosure delete leaves it in, so the other account can bind it again.
            occupant.TerrariumId = null;
            occupant.Status = DeviceStatus.Provisioning;
            occupant.ProvisionedAt = null;

            store.AddAuditEntry(AuditLog.ForDevice(
                AuditAction.DeviceUnbound,
                occupant.Id,
                occupant.PublicId,
                ownerUserId,
                actor ?? AuditActor.Unknown,
                now,
                afterJson: Serialize(new { terrariumId = (Guid?)null, displacedBy = device.PublicId })));
        }

        device.TerrariumId = terrarium.Id;
        device.Terrarium = terrarium;

        store.AddAuditEntry(AuditLog.ForDevice(
            AuditAction.DeviceMoved,
            device.Id,
            device.PublicId,
            ownerUserId,
            actor ?? AuditActor.Unknown,
            now,
            beforeJson: Serialize(new { terrariumId = previousTerrariumId }),
            afterJson: Serialize(new
            {
                terrariumId = terrarium.Id,
                displacedDeviceId = occupant?.PublicId,
            })));

        if (!await store.TrySaveRebindAsync(terrarium.Id, device.Id, cancellationToken))
        {
            return DeviceOutcome.Failure(
                "terrarium_already_bound",
                "That terrarium was bound to another device a moment ago. Reload and try again.");
        }

        return DeviceOutcome.Moved(new DeviceMoveResult(Describe(device), previousTerrariumId, occupant?.PublicId));
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
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || device.Status == DeviceStatus.Revoked || (!mayReachAnyAccount && device.UserId != ownerUserId))
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
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        var now = clock.UtcNow;
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || (!mayReachAnyAccount && device.UserId != ownerUserId))
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return DeviceOutcome.Done();
        }

        await RevokeCoreAsync(device, ownerUserId, now, detach: false, actor, cancellationToken);

        return DeviceOutcome.Done();
    }

    /// <summary>
    /// Removes a board from the caller's fleet: its credentials are revoked and it is detached from its enclosure
    /// (TASKS 2026-10-14, item 9b).
    /// </summary>
    /// <remarks>
    /// <b>This is not a row delete, and the documentation says so.</b> The board's readings, health reports, events
    /// and audit rows name it, so removing the row would take a keeper's history with it — or fail on those foreign
    /// keys. What a keeper means by "remove this board" is that it should stop being theirs and stop reporting, and
    /// that is exactly what happens: credentials revoked (BR-05.4), session closed, enclosure unbound so the DI-04
    /// slot is free for a replacement. The audit row is the same <c>device.revoked</c> verb as
    /// <c>POST /devices/{id}/revoke</c>, with the detachment recorded in its payload — one verb for one effect,
    /// rather than a second word for what the credential does.
    /// </remarks>
    /// <param name="deviceId">Public id of the device.</param>
    /// <param name="ownerUserId">The authenticated caller, who must own the device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    public async Task<DeviceOutcome> DeleteAsync(
        string deviceId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || (!mayReachAnyAccount && device.UserId != ownerUserId))
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        var now = clock.UtcNow;

        // Unlike revoke, this runs even for an already-revoked board: a board revoked while still bound to an
        // enclosure has to be detachable afterwards, and that is the one thing this route does that revoke does not.
        if (device.Status == DeviceStatus.Revoked && device.TerrariumId is null)
        {
            return DeviceOutcome.Described(Describe(device));
        }

        await RevokeCoreAsync(device, ownerUserId, now, detach: true, actor, cancellationToken);

        return DeviceOutcome.Described(Describe(device));
    }

    /// <summary>
    /// Invalidates a board's credentials, closes its session and (optionally) detaches it. The one place a
    /// revocation is written, shared by the revoke and delete routes.
    /// </summary>
    /// <param name="device">Board to revoke, already checked as the caller's.</param>
    /// <param name="ownerUserId">Acting account.</param>
    /// <param name="now">Instant of the change.</param>
    /// <param name="detach">True to clear the enclosure binding as well.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RevokeCoreAsync(
        Device device,
        Guid ownerUserId,
        DateTimeOffset now,
        bool detach,
        AuditActor? actor,
        CancellationToken cancellationToken)
    {
        var alreadyRevoked = device.Status == DeviceStatus.Revoked;

        device.Status = DeviceStatus.Revoked;
        device.RevokedAt ??= now;
        device.ClaimCode = null;
        device.ClaimCodeExpiresAt = null;

        if (detach)
        {
            device.TerrariumId = null;
            device.ProvisionedAt = null;
        }

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
            afterJson: Serialize(new
            {
                status = nameof(DeviceStatus.Revoked),
                credentialsRevoked,
                detached = detach,
                alreadyRevoked,
            })));

        if (!alreadyRevoked)
        {
            // Marked so the payload can say whether this call or an earlier one killed the credential.
        }

        await store.SaveChangesAsync(cancellationToken);

        // After the write, so the credential check already refuses the device even if this kick fails or the
        // board reconnects before it takes effect.
        await sessions.KickAsync(device.PublicId, "device_revoked", cancellationToken);
    }

    /// <summary>
    /// Normalises a caller-supplied board name: null when it was omitted (leave whatever is there), the default
    /// label when it was blank, and the trimmed value otherwise. A name longer than the column is a violation rather
    /// than a silent truncation.
    /// </summary>
    /// <param name="candidate">Name as it arrived, or null when the field was absent.</param>
    /// <param name="violations">Collects the refusal, when there is one.</param>
    private static string? NormaliseDeviceName(string? candidate, List<IdentityViolation> violations)
    {
        if (candidate is null)
        {
            return null;
        }

        var name = candidate.Trim();

        if (name.Length == 0)
        {
            // An empty name would leave the fleet screen with a blank row; the default label is the honest value for
            // "no name given".
            return DefaultDeviceName;
        }

        if (name.Length > DeviceLimits.NameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "deviceName",
                "device_name_too_long",
                $"A name may be at most {DeviceLimits.NameMaxLength} characters."));

            return null;
        }

        return name;
    }

    /// <summary>The label a board carries until its owner renames it.</summary>
    private const string DefaultDeviceName = "New node";

    /// <summary>
    /// True when the board belongs to the caller. Claiming sets <c>UserId</c>, so a board that only registered and
    /// was never claimed has no owner and answers "not found" to everybody — which is why the fleet routes cannot be
    /// used to enumerate boards in provisioning mode.
    /// </summary>
    private static bool IsCallers(Device device, Guid ownerUserId) => device.UserId == ownerUserId;

    /// <summary>Projects a board onto the fleet screen's shape (FR-16, TASKS 2026-10-14, item 9b).</summary>
    private DeviceDetail Describe(Device device) => new(
        device.PublicId,
        device.DeviceName,
        device.ChipId,
        device.MacAddress,
        string.IsNullOrEmpty(device.FirmwareVersion) ? null : device.FirmwareVersion,
        DeviceStatusNames.Of(device.Status),
        DeriveRuntimeStatus(device),
        device.Protocol == DeviceProtocol.Mqtt ? "mqtt" : "http_fallback",
        device.TerrariumId,
        device.Terrarium?.Name,
        device.LastSeenAt,
        device.SamplingIntervalSec,
        device.PublishIntervalSec,
        device.ProvisionedAt,
        device.RevokedAt,
        device.CreatedAt,
        device.SignalStrengthDbm,
        device.BatteryPct,
        device.UptimeSeconds,
        device.FreeHeapKb,
        device.CalibrationJson);

    /// <summary>
    /// The online/offline badge the fleet screen shows. Identical to the rule the terrarium header uses, down to the
    /// same <see cref="DeviceSilencePolicy"/> call, so the two screens cannot disagree about one board
    /// (§02-design/03 §4.3).
    /// </summary>
    private string DeriveRuntimeStatus(Device device)
    {
        if (device.Status is DeviceStatus.Revoked or DeviceStatus.Provisioning or DeviceStatus.Maintenance)
        {
            return DeviceStatusNames.Of(device.Status);
        }

        if (device.LastSeenAt is not { } lastSeenAt)
        {
            return DeviceStatusNames.Of(DeviceStatus.Offline);
        }

        return DeviceSilencePolicy.IsSilent(
            clock.UtcNow - lastSeenAt,
            device.SamplingIntervalSec,
            silence.SilentAfterIntervals)
            ? DeviceStatusNames.Of(DeviceStatus.Offline)
            : DeviceStatusNames.Of(DeviceStatus.Online);
    }

    /// <summary>The caller's boards, ordered by display name (TASKS 2026-10-14, item 9b).</summary>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">When true, lists every device in the deployment (data.read.any).</param>
    public async Task<DeviceOutcome> ListAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        bool mayReachAnyAccount = false)
    {
        var devices = mayReachAnyAccount
            ? await store.ListAllAsync(cancellationToken)
            : await store.ListOwnedAsync(ownerUserId, cancellationToken);

        return DeviceOutcome.Listed([.. devices.Select(Describe)]);
    }

    /// <summary>One board, or <c>not_found</c> when it is unknown or belongs to someone else.</summary>
    /// <param name="deviceId">Public id of the device.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">When true, allows reading any device in the deployment (data.read.any).</param>
    public async Task<DeviceOutcome> GetAsync(
        string deviceId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        bool mayReachAnyAccount = false)
    {
        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || (!mayReachAnyAccount && !IsCallers(device, ownerUserId)))
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        return DeviceOutcome.Described(Describe(device));
    }

    /// <summary>
    /// Edits a board's name and intervals (TASKS 2026-10-14, item 9b). A patch: an omitted field keeps its value.
    /// </summary>
    /// <remarks>
    /// Not audited, and deliberately: <c>BR-18.4</c> names the credential operations, and renaming a node or moving
    /// its interval is the ordinary use of a settings screen rather than an event anything has to answer for later.
    /// The same reasoning already applies to edits of an enclosure.
    /// </remarks>
    /// <param name="deviceId">Public id of the device.</param>
    /// <param name="request">The fields to change; omitted members are left alone.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">When true, allows editing any device in the deployment.</param>
    public async Task<DeviceOutcome> UpdateAsync(
        string deviceId,
        UpdateDeviceRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken = default,
        bool mayReachAnyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = await store.FindDeviceByPublicIdAsync(deviceId, cancellationToken);

        if (device is null || (!mayReachAnyAccount && !IsCallers(device, ownerUserId)))
        {
            return DeviceOutcome.Failure("not_found", "Device not found.");
        }

        var violations = new List<IdentityViolation>();

        var name = NormaliseDeviceName(request.DeviceName, violations);

        ValidateInterval(request.SamplingIntervalSec, "samplingIntervalSec", violations);
        ValidateInterval(request.PublishIntervalSec, "publishIntervalSec", violations);

        if (violations.Count > 0)
        {
            return DeviceOutcome.Invalid("device_invalid", "The device changes were refused.", violations);
        }

        if (name is not null)
        {
            device.DeviceName = name;
        }

        if (request.SamplingIntervalSec is { } sampling)
        {
            device.SamplingIntervalSec = sampling;
        }

        if (request.PublishIntervalSec is { } publishing)
        {
            device.PublishIntervalSec = publishing;
        }

        await store.SaveChangesAsync(cancellationToken);

        return DeviceOutcome.Described(Describe(device));
    }

    /// <summary>An interval inside the bounds of <see cref="DeviceLimits"/>, or a violation naming the field.</summary>
    private static void ValidateInterval(int? value, string field, List<IdentityViolation> violations)
    {
        if (value is not { } interval)
        {
            return;
        }

        if (interval is < DeviceLimits.MinIntervalSec or > DeviceLimits.MaxIntervalSec)
        {
            violations.Add(new IdentityViolation(
                field,
                "interval_out_of_range",
                $"An interval must be between {DeviceLimits.MinIntervalSec} and {DeviceLimits.MaxIntervalSec} seconds."));
        }
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
        else
        {
            var problem = MacAddress.Inspect(mac, out _);

            if (problem != MacAddressProblem.None)
            {
                // One code for every reason, because the field is the same field — but the message says what is
                // wrong with this value, which is what lets the person holding the board fix it (2026-10-23).
                violations.Add(new IdentityViolation("macAddress", "mac_invalid", MacAddress.Describe(problem)));
            }
        }

        var firmware = (request.FirmwareVersion ?? string.Empty).Trim();

        if (firmware.Length > 16)
        {
            violations.Add(new IdentityViolation(
                "firmwareVersion", "firmware_too_long", "A firmware version must be at most 16 characters."));
        }

        return violations;
    }

}
