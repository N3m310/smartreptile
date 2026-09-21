using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartReptile.Api.Hubs;

/// <summary>
/// Real-time channel used by the app and the web dashboard for live values, device status and alert badges
/// (FR-08, ADR-011). History stays on REST; the hub only pushes changes.
/// </summary>
/// <remarks>
/// Group membership MUST be authorised before joining: an unchecked <c>JoinTerrarium</c> would turn the hub
/// into a cross-tenant data leak (§03-implementation/03 §7). The membership check lands with the terrarium
/// API in M2; until then the hub refuses to join anything.
/// </remarks>
[Authorize]
public sealed class TelemetryHub(ILogger<TelemetryHub> logger) : Hub
{
    /// <summary>Starts pushing updates for one terrarium.</summary>
    /// <param name="terrariumId">Terrarium the caller wants updates for.</param>
    public async Task JoinTerrarium(Guid terrariumId)
    {
        // TODO(M2): resolve the caller's role for this terrarium through the authorisation handler and reject
        // with a HubException when the caller is not a member.
        logger.LogWarning(
            "JoinTerrarium({TerrariumId}) refused for connection {ConnectionId}: membership checks are not wired yet (M2)",
            terrariumId,
            Context.ConnectionId);

        throw new HubException("Membership checks are not available yet.");
    }

    /// <summary>Stops pushing updates for one terrarium.</summary>
    /// <param name="terrariumId">Terrarium to unsubscribe from.</param>
    public Task LeaveTerrarium(Guid terrariumId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(terrariumId));

    /// <summary>Conventional group name for a terrarium.</summary>
    public static string GroupName(Guid terrariumId) => $"terrarium:{terrariumId}";
}
