using Microsoft.Data.SqlClient;
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
    /// <summary>
    /// The filtered unique index DI-04 is built on. Its name is what distinguishes a lost claim race from any other
    /// constraint, because the SQL error number alone also covers the public-id and chip-id indexes.
    /// </summary>
    private const string TerrariumBindingIndex = "IX_Device_TerrariumId";

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

    /// <inheritdoc />
    public async Task<bool> TrySaveClaimAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsTerrariumBindingConflict(ex))
        {
            // Two claims of one terrarium both passed the pre-check and the database refused the second binding.
            // That is DI-04 working, not an incident: the caller answers the documented 409 instead of a 500.
            return false;
        }
    }

    /// <summary>
    /// True when the failure is the DI-04 binding index and not another unique constraint. The index name is
    /// checked rather than the error number alone, because 2601/2627 also cover the public-id and chip-id indexes —
    /// treating one of those as "a device bound first" would hide a real bug behind a plausible answer.
    /// </summary>
    private static bool IsTerrariumBindingConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && sql.Number is 2601 or 2627
        && sql.Message.Contains(TerrariumBindingIndex, StringComparison.Ordinal);
}
