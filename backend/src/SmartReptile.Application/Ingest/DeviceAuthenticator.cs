using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Ingest;

/// <summary>Result of the authentication stage: a device whose rows the pipeline may write, or a refusal.</summary>
/// <param name="Device">The tracked device, when the stage accepted the batch.</param>
/// <param name="Problem">The refusal, when it did not.</param>
public sealed record DeviceAuthentication(Device? Device, IngestProblem? Problem)
{
    /// <summary>True when a device came back.</summary>
    public bool IsValid => Device is not null;

    /// <summary>An accepted batch.</summary>
    public static DeviceAuthentication Accepted(Device device) => new(device, null);

    /// <summary>A refusal. One code, so a caller cannot turn it into an existence oracle (BR-02.2).</summary>
    public static DeviceAuthentication Refused(string message) =>
        new(null, new IngestProblem("auth_failed", message));
}

/// <summary>
/// Stage 2 of the ingest pipeline: is this batch really from the device it claims, and may it be stored?
/// (rules V-04/V-05, TC-U-05).
/// </summary>
/// <remarks>
/// On MQTT the credential was already checked when the socket was accepted (§02-design/06 §4.2), so this stage
/// re-establishes the things that can change <i>after</i> a session was opened and that the broker's CONNECT-time
/// check cannot see: the payload naming a device the session did not authenticate as, and a device that has been
/// revoked or unbound since. On the HTTPS fallback the same class does the full secret comparison, which is why
/// it takes the presented secret as an optional argument rather than assuming a transport.
/// <para>
/// Every refusal is the same outward code. A device that learns "revoked" and a device that learns "no such
/// device" between them get a working probe for which public ids exist (§02-design/06 §7).
/// </para>
/// </remarks>
public sealed class DeviceAuthenticator(
    ITelemetryStore store,
    IDeviceCredentials credentials,
    IClock clock)
{
    /// <summary>Checks the payload's device, its lifecycle state, and — when one was presented — its secret.</summary>
    /// <param name="batch">The validated batch.</param>
    /// <param name="transportDevicePublicId">Device the transport authenticated; the topic's id.</param>
    /// <param name="presentedSecret">Secret from the request, when the transport carries one (HTTPS fallback).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceAuthentication> AuthenticateAsync(
        TelemetryBatch batch,
        string transportDevicePublicId,
        string? presentedSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (!string.Equals(batch.DevicePublicId, transportDevicePublicId, StringComparison.OrdinalIgnoreCase))
        {
            return DeviceAuthentication.Refused(
                $"the payload names device '{batch.DevicePublicId}' but the transport authenticated '{transportDevicePublicId}'");
        }

        var device = await store.FindDeviceAsync(batch.DevicePublicId, cancellationToken).ConfigureAwait(false);

        if (device is null)
        {
            return DeviceAuthentication.Refused($"no usable device matches '{batch.DevicePublicId}'");
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return DeviceAuthentication.Refused($"device '{batch.DevicePublicId}' is revoked");
        }

        if (device.TerrariumId is null)
        {
            // A sample's TerrariumId is a required foreign key and the sample is attributed to the terrarium at
            // ingest (so rebinding never rewrites history). Without a binding there is nothing to attribute it to.
            return DeviceAuthentication.Refused($"device '{batch.DevicePublicId}' is not bound to a terrarium");
        }

        if (presentedSecret is not null && !MatchesAnyCredential(device, presentedSecret))
        {
            return DeviceAuthentication.Refused($"the presented secret does not match device '{batch.DevicePublicId}'");
        }

        return DeviceAuthentication.Accepted(device);
    }

    /// <summary>
    /// True when the secret matches a credential that is usable right now. Any match wins: rotation deliberately
    /// leaves the previous credential valid for its grace window (BR-05.5), so a device mid-rotation is not
    /// locked out by the very operation that was supposed to be seamless.
    /// </summary>
    private bool MatchesAnyCredential(Device device, string presentedSecret)
    {
        var now = clock.UtcNow;

        return device.Credentials.Any(credential =>
            credential.IsUsableAt(now)
            && credentials.VerifySecret(presentedSecret, credential.SecretHash, credential.Salt));
    }
}
