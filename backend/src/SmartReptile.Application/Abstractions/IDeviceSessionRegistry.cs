namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Who is connected to the broker right now, and the ability to close those connections. BR-05.4 requires a
/// revoked device to be disconnected, and only the broker can do that, so the use case asks through this port
/// instead of reaching into MQTTnet.
/// </summary>
/// <remarks>
/// Kept deliberately small: the registry knows which sessions a device has, not what a session is. A device may
/// hold more than one connection (a reconnect racing its own LWT), so closing is counted rather than boolean.
/// </remarks>
public interface IDeviceSessionRegistry
{
    /// <summary>Closes every session belonging to the device. Returns how many were closed.</summary>
    Task<int> KickAsync(string devicePublicId, string reason, CancellationToken cancellationToken = default);

    /// <summary>True when the device has at least one open session.</summary>
    bool IsConnected(string devicePublicId);

    /// <summary>Number of devices with at least one open session.</summary>
    int ConnectedDevices { get; }
}
