using SmartReptile.Application.Devices;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// In-memory <see cref="IDeviceSilenceStore"/>. Returns the same device and alert instances the real store would
/// track, so a test can see the lifecycle state and the alert row a sweep moved — and counts its saves, because a
/// sweep that staged nothing must not be the only one that skips the commit.
/// </summary>
internal sealed class FakeDeviceSilenceStore : IDeviceSilenceStore
{
    private readonly List<WatchedDevice> _watched = [];

    public IReadOnlyList<Alert> Added => _added;

    public int SaveCount { get; private set; }

    private readonly List<Alert> _added = [];

    public void Watch(Device device, Alert? openAlert = null) => _watched.Add(new WatchedDevice(device, openAlert));

    public Task<IReadOnlyList<WatchedDevice>> FindWatchedDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WatchedDevice>>(_watched);

    public void AddAlert(Alert alert) => _added.Add(alert);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
