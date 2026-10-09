namespace SmartReptile.Application.Ingest;

/// <summary>
/// The <c>statusChanged</c> event of `07-appendices/03` §6: what a subscribed client receives when a device moves
/// between <c>online</c>, <c>offline</c> and <c>maintenance</c>.
/// </summary>
/// <param name="TerrariumId">Terrarium the group is named after.</param>
/// <param name="DeviceId">Public id of the device, not its surrogate key — the clients never see the key.</param>
/// <param name="Status">New state: <c>online</c>, <c>offline</c> or <c>maintenance</c>.</param>
/// <param name="LastSeenAt">When the server last heard from it, so a client can date the badge it just changed.</param>
public sealed record StatusChangedPayload(
    Guid TerrariumId,
    string DeviceId,
    string Status,
    DateTimeOffset LastSeenAt)
{
    /// <summary>Projects one committed transition onto the documented shape.</summary>
    /// <param name="changed">The transition as the channel pipeline reported it.</param>
    public static StatusChangedPayload From(DeviceStatusChanged changed) => new(
        changed.TerrariumId,
        changed.DevicePublicId,
        changed.Status,
        changed.LastSeenAt);
}
