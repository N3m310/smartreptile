using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Notifications;

/// <summary>
/// The Firebase Cloud Messaging channel (FR-13, §02-design/05 §3): HTTP v1 with a service-account credential.
/// </summary>
/// <remarks>
/// <b>Why HTTP v1 and not the Firebase Admin SDK.</b> The SDK brings a dependency tree (gRPC, protobuf, Google
/// auth libraries) for one POST, and the credential it would read is the same JSON this class reads. HTTP v1 is the
/// documented long-term API — the legacy server key that used to be the simple path is retired — so the honest
/// trade is a small amount of auth code for a dependency this project would otherwise carry everywhere.
/// <para>
/// <b>A refused token is not a failed delivery.</b> FCM answers <c>UNREGISTERED</c> (or <c>NOT_FOUND</c>) when the
/// app is gone from the device; retrying cannot help, so that answer switches the account's push channel off
/// instead (TC-U-44). Every other refusal is retried on BR-13.5's schedule by the dispatcher, which this class
/// deliberately knows nothing about.
/// </para>
/// <para>
/// <b>Nothing here throws into the pipeline.</b> An exception is a refusal with a message: the dispatcher records
/// the attempt and moves on, because a push service having a bad afternoon must not stop an inbox row existing.
/// </para>
/// </remarks>
public sealed class FcmNotificationChannel(
    IOptions<NotificationOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<FcmNotificationChannel> logger) : INotificationChannel
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly FcmOptions _settings = options.Value.Fcm;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private (string ProjectId, string ClientEmail, string PrivateKey)? _credential;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Fcm;

    /// <inheritdoc />
    public async Task<NotificationSendResult> SendAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return NotificationSendResult.Failed("the FCM channel is disabled in this deployment");
        }

        try
        {
            var credential = ReadCredential();
            var token = await AccessTokenAsync(cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    token = delivery.Destination,
                    notification = new { title = delivery.Title, body = delivery.Body },
                    data = new
                    {
                        deepLink = delivery.DeepLink ?? string.Empty,
                        userId = delivery.UserId.ToString(),
                    },
                    android = new { priority = "high" },
                },
            });

            using var client = httpClientFactory.CreateClient(nameof(FcmNotificationChannel));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{credential.ProjectId}/messages:send")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return NotificationSendResult.Ok();
            }

            var error = Describe(response.StatusCode, body);

            return IsGone(body)
                ? NotificationSendResult.Gone(error)
                : NotificationSendResult.Failed(error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FCM delivery threw; the attempt is recorded as a failure");

            return NotificationSendResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// True when FCM says the registration token no longer exists. Both spellings appear in the wild: v1 answers
    /// <c>UNREGISTERED</c> in <c>error.details[].errorCode</c> for a token that was revoked, and <c>NOT_FOUND</c> for
    /// one that never existed.
    /// </summary>
    /// <param name="body">Response body.</param>
    private static bool IsGone(string body) =>
        body.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)
        || body.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private static string Describe(HttpStatusCode status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return NotificationLog.Truncate($"{(int)status} {text}", NotificationLog.LastErrorMaxLength);
            }
        }
        catch (JsonException)
        {
            // A relay or a proxy answered instead of FCM; the status line is what we have.
        }

        return NotificationLog.Truncate($"{(int)status} {body}", NotificationLog.LastErrorMaxLength);
    }

    /// <summary>Reads and validates the service-account file once, keeping it in memory afterwards.</summary>
    private (string ProjectId, string ClientEmail, string PrivateKey) ReadCredential()
    {
        if (_credential is { } cached)
        {
            return cached;
        }

        var path = _settings.ServiceAccountJsonPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                "Notifications:Fcm:ServiceAccountJsonPath must name the mounted service-account file.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var credential = (
            ProjectId: root.GetProperty("project_id").GetString() ?? throw new InvalidOperationException("project_id is missing"),
            ClientEmail: root.GetProperty("client_email").GetString() ?? throw new InvalidOperationException("client_email is missing"),
            PrivateKey: root.GetProperty("private_key").GetString() ?? throw new InvalidOperationException("private_key is missing"));

        _credential = credential;

        return credential;
    }

    /// <summary>
    /// Returns a bearer token, refreshing it a minute before it expires. Serialised behind a semaphore so a sweep
    /// that fans out to several accounts does not fetch one token per recipient.
    /// </summary>
    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is { } token && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return token;
        }

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_accessToken is { } refreshed && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return refreshed;
            }

            var credential = ReadCredential();
            var assertion = ServiceAccountAssertion.Create(credential.ClientEmail, credential.PrivateKey, TokenEndpoint, MessagingScope);

            using var client = httpClientFactory.CreateClient(nameof(FcmNotificationChannel));
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            });

            using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(body);
            var accessToken = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("the token response carried no access_token");

            var lifetime = document.RootElement.TryGetProperty("expires_in", out var expiresIn)
                ? TimeSpan.FromSeconds(expiresIn.GetInt32())
                : TimeSpan.FromMinutes(55);

            _accessToken = accessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow + lifetime;

            return accessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }
}
