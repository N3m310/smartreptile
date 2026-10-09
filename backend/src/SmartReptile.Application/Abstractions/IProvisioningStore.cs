using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Terrariums;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Persistence port for the onboarding flow. One port rather than one per entity because these queries exist only
/// to serve UC-01, and splitting them would spread one use case across four interfaces
/// (§03-implementation/02 §2.1).
/// </summary>
public interface IProvisioningStore
{
    /// <summary>
    /// A device with its credential history, found by the public id the API hands out. Rotation, revocation and
    /// verification all need the whole history, so it is loaded here rather than lazily per call.
    /// </summary>
    Task<Device?> FindDeviceByPublicIdAsync(string publicId, CancellationToken cancellationToken);

    /// <summary>Looks a board up by its chip id, which is what makes re-registration idempotent.</summary>
    Task<Device?> FindDeviceByChipIdAsync(string chipId, CancellationToken cancellationToken);

    /// <summary>Looks a device up by claim code. Callers must not reveal whether a row was found (BR-04.3).</summary>
    Task<Device?> FindDeviceByClaimCodeAsync(string claimCode, CancellationToken cancellationToken);

    /// <summary>True when the public id is taken — the unique index is the enforcement, this is the retry check.</summary>
    Task<bool> PublicIdExistsAsync(string publicId, CancellationToken cancellationToken);

    /// <summary>Stages a new device.</summary>
    void AddDevice(Device device);

    /// <summary>Stages a new credential.</summary>
    void AddCredential(DeviceCredential credential);

    /// <summary>
    /// Stages an audit row. It lives on this port rather than one of its own so the row is committed by the same
    /// <see cref="SaveChangesAsync"/> as the change it describes (BR-18.4): an audited change that commits without
    /// its entry is worse than no trail at all, because the gap reads as "nothing happened".
    /// </summary>
    void AddAuditEntry(AuditLog entry);

    /// <summary>
    /// The terrarium, but only when it belongs to <paramref name="ownerUserId"/> and is not soft-deleted.
    /// A foreign or missing id therefore looks identical, which is what keeps ids from being probed (BR-02.2).
    /// </summary>
    Task<Terrarium?> FindOwnedTerrariumAsync(Guid terrariumId, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>True when an active device is already bound to the terrarium (DI-04, enforced by a filtered index).</summary>
    Task<bool> TerrariumHasDeviceAsync(Guid terrariumId, CancellationToken cancellationToken);

    /// <summary>Commits staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits a claim, reporting a lost race on the DI-04 binding index instead of throwing. False means another
    /// claim bound a device to that terrarium between the pre-check and this write, which is the
    /// <c>terrarium_already_bound</c> the caller must answer rather than a 500 (TC-I-05).
    /// </summary>
    Task<bool> TrySaveClaimAsync(CancellationToken cancellationToken);
}
