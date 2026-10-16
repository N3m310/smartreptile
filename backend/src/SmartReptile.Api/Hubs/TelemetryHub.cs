using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartReptile.Api.Security;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Hubs;

/// <summary>
/// Real-time channel used by the app and the web dashboard for live values, device status and alert badges
/// (FR-08, ADR-011). History stays on REST; the hub only pushes changes.
/// </summary>
/// <remarks>
/// Group membership is authorised before a join, because an unchecked <c>JoinTerrarium</c> would turn the hub into
/// a cross-tenant data leak: the group name is derived from the terrarium id, so joining one is asking to receive
/// its data. The caller must own the terrarium, and a foreign id is refused with the same message as a
/// non-existent one, so the hub cannot be used to enumerate ids either (BR-02.2). Contract:
/// <c>07-appendices/03</c> §6.
/// </remarks>
[Authorize]
public sealed class TelemetryHub(TerrariumService terrariums, ILogger<TelemetryHub> logger) : Hub
{
    /// <summary>
    /// One message for "not yours" and for "does not exist". The hub deliberately keeps BR-02.2's single answer even
    /// though the REST surface now tells the three cases apart: a refusal message is the only thing a client can
    /// learn here, and a socket that names ids could then be used to probe for them (TASKS 2026-10-14, item 3).
    /// </summary>
    private const string RefusalMessage = "Terrarium not found.";

    /// <summary>Starts pushing updates for one terrarium.</summary>
    /// <param name="terrariumId">Terrarium the caller wants updates for.</param>
    /// <exception cref="HubException">
    /// The caller does not own the terrarium — unless they are the administrator, who may follow any account's
    /// enclosure — or it does not exist.
    /// </exception>
    public async Task JoinTerrarium(Guid terrariumId)
    {
        var userId = Context.User?.GetUserId();

        // A connection without a subject should not exist, because [Authorize] runs before this method — but
        // treating it as a refusal costs nothing and is the safe direction to be wrong in.
        if (userId is null ||
            !await terrariums.IsMemberAsync(
                terrariumId,
                userId.Value,
                Context.ConnectionAborted,
                Context.User?.HasPermission(Permissions.DataReadAny) ?? false))
        {
            logger.LogWarning(
                "JoinTerrarium({TerrariumId}) refused for connection {ConnectionId}",
                terrariumId,
                Context.ConnectionId);

            throw new HubException(RefusalMessage);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(terrariumId), Context.ConnectionAborted);
    }

    /// <summary>Stops pushing updates for one terrarium.</summary>
    /// <param name="terrariumId">Terrarium to unsubscribe from.</param>
    public Task LeaveTerrarium(Guid terrariumId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(terrariumId), Context.ConnectionAborted);

    /// <summary>Conventional group name for a terrarium.</summary>
    public static string GroupName(Guid terrariumId) => $"terrarium:{terrariumId}";
}
