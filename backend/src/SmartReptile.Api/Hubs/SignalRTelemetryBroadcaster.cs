using Microsoft.AspNetCore.SignalR;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Api.Hubs;

/// <summary>
/// The SignalR push of FR-08/FR-09 — roadmap task 2.9. Fan-out happens after the commit, so a client that misses
/// a push has missed a notification and never a measurement.
/// </summary>
/// <remarks>
/// It lives in the API project because that is the only layer that can see <see cref="TelemetryHub"/>; the ingest
/// pipeline depends on the <see cref="ITelemetryBroadcaster"/> port, not on this type. One event per sample: the
/// documented payload is a single sample, so a client's handler treats every arrival the same way rather than
/// unwrapping a batch that may be one long.
/// <para>
/// The group carries no membership of its own — <see cref="TelemetryHub.JoinTerrarium"/> authorises before it
/// joins — so pushing to a group can only reach a connection that has already proven it owns the terrarium.
/// </para>
/// </remarks>
public sealed class SignalRTelemetryBroadcaster(IHubContext<TelemetryHub> hub) : ITelemetryBroadcaster
{
    /// <inheritdoc />
    public async Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken)
    {
        foreach (var sample in samples)
        {
            await hub.Clients
                .Group(TelemetryHub.GroupName(sample.TerrariumId))
                .SendAsync("readingAdded", ReadingAddedPayload.From(sample), cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task BroadcastStatusAsync(DeviceStatusChanged statusChanged, CancellationToken cancellationToken) =>
        hub.Clients
            .Group(TelemetryHub.GroupName(statusChanged.TerrariumId))
            .SendAsync("statusChanged", StatusChangedPayload.From(statusChanged), cancellationToken);
}
