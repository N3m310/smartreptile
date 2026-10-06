using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Terrariums;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// The <see cref="IProvisioningStore"/> adapter. Credentials are loaded eagerly where the caller needs them —
/// rotation, revocation and verification all have to look at the whole credential history, and a lazy load would
/// turn one query into N.
/// </summary>
public sealed class EfProvisioningStore(SmartReptileDbContext db) : IProvisioningStore
{
    /// <inheritdoc />
    public Task<Device?> FindDeviceByPublicIdAsync(string publicId, CancellationToken cancellationToken) =>
        db.Devices
            .Include(device => device.Credentials)
            .FirstOrDefaultAsync(device => device.PublicId == publicId, cancellationToken);

    /// <inheritdoc />
    public Task<Device?> FindDeviceByChipIdAsync(string chipId, CancellationToken cancellationToken) =>
        db.Devices.FirstOrDefaultAsync(device => device.ChipId == chipId, cancellationToken);

    /// <inheritdoc />
    public Task<Device?> FindDeviceByClaimCodeAsync(string claimCode, CancellationToken cancellationToken) =>
        db.Devices.FirstOrDefaultAsync(device => device.ClaimCode == claimCode, cancellationToken);

    /// <inheritdoc />
    public Task<bool> PublicIdExistsAsync(string publicId, CancellationToken cancellationToken) =>
        db.Devices.AnyAsync(device => device.PublicId == publicId, cancellationToken);

    /// <inheritdoc />
    public void AddDevice(Device device) => db.Devices.Add(device);

    /// <inheritdoc />
    public void AddCredential(DeviceCredential credential) => db.DeviceCredentials.Add(credential);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <inheritdoc />
    public Task<Terrarium?> FindOwnedTerrariumAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        db.Terrariums.FirstOrDefaultAsync(
            terrarium => terrarium.Id == terrariumId
                         && terrarium.UserId == ownerUserId
                         && terrarium.DeletedAt == null,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> TerrariumHasDeviceAsync(Guid terrariumId, CancellationToken cancellationToken) =>
        db.Devices.AnyAsync(
            device => device.TerrariumId == terrariumId && device.Status != DeviceStatus.Revoked,
            cancellationToken);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
