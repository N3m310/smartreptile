using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// EF Core adapter for the persistence the ingest pipeline needs (stage 5/6 of §02-design/03 §1).
/// </summary>
/// <remarks>
/// One scoped context per batch: the device row, its sample rows and its health row are staged together and
/// committed by a single <see cref="SaveChangesAsync"/>, so a batch can never leave the device's
/// <c>LastSeenAt</c> ahead of the samples that caused it (§02-design/03 §7).
/// </remarks>
public sealed class EfTelemetryStore(SmartReptileDbContext db, ILogger<EfTelemetryStore> logger) : ITelemetryStore
{
    /// <summary>The filtered unique index DI-02 is built on; the name is what identifies a dedupe race.</summary>
    private const string SequenceIndex = "IX_TelemetrySample_DeviceId_Sequence";

    /// <inheritdoc />
    public Task<Device?> FindDeviceAsync(string publicId, CancellationToken cancellationToken) =>
        db.Devices
            // Credentials are loaded eagerly because the HTTPS fallback verifies a presented secret against them,
            // and a lazy load inside the pipeline would be a second round trip per batch.
            .Include(device => device.Credentials)
            .FirstOrDefaultAsync(device => device.PublicId == publicId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<long>> FindExistingSequencesAsync(
        Guid deviceId,
        long first,
        long last,
        CancellationToken cancellationToken)
    {
        var sequences = await db.TelemetrySamples
            .Where(sample => sample.DeviceId == deviceId && sample.Sequence >= first && sample.Sequence <= last)
            .Select(sample => sample.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sequences.ToHashSet();
    }

    /// <inheritdoc />
    public void AddSample(TelemetrySample sample) => db.TelemetrySamples.Add(sample);

    /// <inheritdoc />
    public void AddHealthSample(DeviceHealthSample health) => db.DeviceHealthSamples.Add(health);

    /// <inheritdoc />
    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateSequence(ex))
        {
            // At-least-once delivery means two writers can hold the same sequence. This is the database doing its
            // job, not an incident: report it so the caller can re-run the dedupe check and count the rows that
            // won instead of failing the batch (TC-I-02).
            logger.LogInformation(
                "Telemetry batch lost the dedupe race on {Index}; re-reading the existing sequences",
                SequenceIndex);

            DiscardStagedChanges();
            return false;
        }
    }

    /// <summary>
    /// Drops everything this attempt staged while leaving the device row tracked. Detaching the device too would
    /// lose the <c>LastSeenAt</c> update the retry re-applies — and a batch that is a duplicate is still proof the
    /// device is alive.
    /// </summary>
    private void DiscardStagedChanges()
    {
        var staged = db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Where(entry => entry.Entity is TelemetrySample or MetricReading or DeviceHealthSample)
            .ToList();

        foreach (var entry in staged)
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in db.ChangeTracker.Entries<Device>().Where(entry => entry.State == EntityState.Modified))
        {
            entry.State = EntityState.Unchanged;
        }
    }

    /// <summary>
    /// True when the failure is a duplicate on the sample-sequence index and not some other constraint. The name
    /// is checked rather than the error number alone, because 2601/2627 also cover the alerts and thresholds
    /// invariants — treating one of those as "duplicate telemetry" would hide a real bug.
    /// </summary>
    private static bool IsDuplicateSequence(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && sql.Number is 2601 or 2627
        && sql.Message.Contains(SequenceIndex, StringComparison.Ordinal);
}
