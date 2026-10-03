using System.Collections.Concurrent;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Mqtt;

/// <summary>
/// The in-process session registry (FR-05 BR-05.4). The broker populates it on connect and disconnect and installs
/// the delegate that closes a live session; the provisioning use case only ever calls
/// <see cref="KickAsync"/>, which is why this type can live in Infrastructure while the port it satisfies
/// sits in Application.
/// </summary>
public sealed class DeviceSessionRegistry : IDeviceSessionRegistry
{
    // Keyed by public device id, then by broker client id: one device may briefly hold two connections.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    // The reverse index, so a publish or subscribe event can be attributed to a device from its client id alone.
    private readonly ConcurrentDictionary<string, string> _devicesByClient = new(StringComparer.Ordinal);

    private Func<string, string, CancellationToken, Task>? _disconnect;

    /// <inheritdoc />
    public int ConnectedDevices => _sessions.Count;

    /// <summary>
    /// Called by the broker once the server exists, so a kick can reach a live connection. The delegate takes
    /// (device id, client id, cancellation token).
    /// </summary>
    public void UseDisconnect(Func<string, string, CancellationToken, Task> disconnect) => _disconnect = disconnect;

    /// <summary>Records an open session. Returns the number of sessions the device now holds.</summary>
    public int Connect(string devicePublicId, string clientId)
    {
        var clients = _sessions.GetOrAdd(
            devicePublicId,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        clients[clientId] = 0;
        _devicesByClient[clientId] = devicePublicId;

        return clients.Count;
    }

    /// <summary>The device a broker client id belongs to, or null when that client is not a device.</summary>
    public string? DeviceOf(string? clientId) =>
        clientId is not null && _devicesByClient.TryGetValue(clientId, out var devicePublicId) ? devicePublicId : null;

    /// <summary>Forgets a session; the device is removed once its last connection is gone.</summary>
    public void Disconnect(string devicePublicId, string clientId)
    {
        _devicesByClient.TryRemove(clientId, out _);

        if (!_sessions.TryGetValue(devicePublicId, out var clients))
        {
            return;
        }

        clients.TryRemove(clientId, out _);

        if (clients.IsEmpty)
        {
            // Remove only the empty entry we saw, so a concurrent Connect is not discarded.
            _sessions.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(devicePublicId, clients));
        }
    }

    /// <inheritdoc />
    public bool IsConnected(string devicePublicId) =>
        _sessions.TryGetValue(devicePublicId, out var clients) && !clients.IsEmpty;

    /// <inheritdoc />
    public async Task<int> KickAsync(
        string devicePublicId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(devicePublicId, out var clients) || _disconnect is null)
        {
            return 0;
        }

        var closed = 0;

        foreach (var clientId in clients.Keys)
        {
            try
            {
                await _disconnect(devicePublicId, clientId, cancellationToken).ConfigureAwait(false);
                closed++;
            }
            catch (Exception)
            {
                // A session that is already gone is not a failure of the revoke: the credential is what actually
                // stops the device, and a connection that survived is refused on its next publish.
            }
            finally
            {
                Disconnect(devicePublicId, clientId);
            }
        }

        return closed;
    }
}
