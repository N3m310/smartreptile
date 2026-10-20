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

    /// <summary>
    /// Looks a device up by its MAC address, in the canonical form <see cref="MacAddress.TryNormalise"/> produces
    /// (TASKS 2026-10-14, item 8). Case-insensitive, because a board that self-registered with lower-case hex is the
    /// same board as one named in upper case.
    /// </summary>
    /// <remarks>
    /// Deliberately unscoped by owner: an unclaimed device has no owner yet — that is what claiming sets — so there
    /// is no account this query could be restricted to. The endpoint that uses it is Owner-gated and the answer for
    /// a MAC that is not claimable is a single <c>device_not_found</c>, so the query cannot be used to enumerate.
    /// </remarks>
    Task<Device?> FindDeviceByMacAddressAsync(string macAddress, CancellationToken cancellationToken);

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

    /// <summary>
    /// The caller's boards, ordered by display name, with the bound terrarium loaded so the list can name it
    /// (TASKS 2026-10-14, item 9b).
    /// </summary>
    /// <remarks>
    /// Scoped by <c>UserId</c>, which is set at claim time. A board that registered but was never claimed has no
    /// owner and therefore belongs to nobody's fleet yet — it appears in the list only once it is bound.
    /// </remarks>
    Task<IReadOnlyList<Device>> ListOwnedAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// All boards in the deployment with the bound terrarium loaded. Used when an administrator lists devices (data.read.any).
    /// </summary>
    Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>Commits staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits a claim, reporting a lost race on the DI-04 binding index instead of throwing. False means another
    /// claim bound a device to that terrarium between the pre-check and this write, which is the
    /// <c>terrarium_already_bound</c> the caller must answer rather than a 500 (TC-I-05).
    /// </summary>
    Task<bool> TrySaveClaimAsync(CancellationToken cancellationToken);
}
