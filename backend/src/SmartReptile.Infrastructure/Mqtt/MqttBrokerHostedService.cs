using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using MQTTnet.Server.Disconnecting;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;

namespace SmartReptile.Infrastructure.Mqtt;

/// <summary>MQTT broker settings (§03-implementation/01 §5, §07-appendices/03 §3).</summary>
public sealed class MqttBrokerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mqtt";

    /// <summary>Interface the TLS listener binds. Use <c>0.0.0.0</c> in a container, loopback for a local run.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Plaintext MQTT port.</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Port used when TLS is enabled.</summary>
    public int TlsPort { get; set; } = 8883;

    /// <summary>
    /// Interface the plaintext listener binds. Loopback by default: BR-05.1 says plaintext exists for local
    /// debugging, so it must not be reachable from the network. A container needs <c>0.0.0.0</c> here, because
    /// Docker's port proxy connects to the container's interface rather than its loopback — that case is
    /// restricted again at the published port (<c>127.0.0.1:1883:1883</c> in <c>docker-compose.yml</c>).
    /// </summary>
    public string PlaintextHost { get; set; } = "127.0.0.1";

    /// <summary>Serve MQTT over TLS (FR-05 BR-05.1). Required for the release runbook.</summary>
    public bool EnableTls { get; set; }

    /// <summary>Do not open the plaintext listener at all (required in release, BR-05.1).</summary>
    public bool DisablePlaintextEndpoint { get; set; }

    /// <summary>Path to the server certificate (PFX) used for TLS.</summary>
    public string? ServerCertificatePath { get; set; }

    /// <summary>Password for the PFX file, if any.</summary>
    public string? ServerCertificatePassword { get; set; }

    /// <summary>Reject connections that do not carry a device credential (BR-05.1).</summary>
    public bool RequireClientAuthentication { get; set; } = true;

    /// <summary>Topic the broker uses to announce a device's unexpected disconnect.</summary>
    public string DeviceTopicPrefix { get; set; } = MqttTopicScheme.DefaultPrefix;
}

/// <summary>Live view of the in-process broker, used by the readiness probe and the metrics endpoint.</summary>
public interface IMqttBrokerStatus
{
    /// <summary>True once the listener is accepting connections.</summary>
    bool IsRunning { get; }

    /// <summary>Number of currently connected clients.</summary>
    int ConnectedClients { get; }

    /// <summary>Rejected connection attempts since start (credential failures, FR-05).</summary>
    long RejectedConnections { get; }

    /// <summary>Messages published by devices since start.</summary>
    long PublishedMessages { get; }

    /// <summary>Publications refused by the topic ACL (§07-appendices/03 §3.1) since start.</summary>
    long RefusedPublications { get; }

    /// <summary>Subscriptions refused by the topic ACL since start.</summary>
    long RefusedSubscriptions { get; }

    /// <summary>Sessions closed by a revoke or a refused publication (BR-05.4).</summary>
    long KickedSessions { get; }

    /// <summary>Last error reported by the broker, if any.</summary>
    string? LastError { get; }
}

/// <summary>In-process broker status holder shared between the hosted service, health check and metrics endpoint.</summary>
public sealed class MqttBrokerStatus : IMqttBrokerStatus
{
    private long _connectedClients;
    private long _rejectedConnections;
    private long _publishedMessages;
    private long _refusedPublications;
    private long _refusedSubscriptions;
    private long _kickedSessions;
    private volatile string? _lastError;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public int ConnectedClients => (int)Interlocked.Read(ref _connectedClients);

    /// <inheritdoc />
    public long RejectedConnections => Interlocked.Read(ref _rejectedConnections);

    /// <inheritdoc />
    public long PublishedMessages => Interlocked.Read(ref _publishedMessages);

    /// <inheritdoc />
    public long RefusedPublications => Interlocked.Read(ref _refusedPublications);

    /// <inheritdoc />
    public long RefusedSubscriptions => Interlocked.Read(ref _refusedSubscriptions);

    /// <inheritdoc />
    public long KickedSessions => Interlocked.Read(ref _kickedSessions);

    /// <inheritdoc />
    public string? LastError => _lastError;

    internal void SetRunning(bool running) => IsRunning = running;

    internal void ClientConnected() => Interlocked.Increment(ref _connectedClients);

    internal void ClientDisconnected() => Interlocked.Decrement(ref _connectedClients);

    internal void ConnectionRejected(string reason)
    {
        Interlocked.Increment(ref _rejectedConnections);
        _lastError = reason;
    }

    internal void MessagePublished() => Interlocked.Increment(ref _publishedMessages);

    internal void PublicationRefused() => Interlocked.Increment(ref _refusedPublications);

    internal void SubscriptionRefused() => Interlocked.Increment(ref _refusedSubscriptions);

    internal void SessionKicked() => Interlocked.Increment(ref _kickedSessions);

    internal void RecordError(string error) => _lastError = error;
}

/// <summary>
/// Hosts the MQTT broker inside the API process (ADR-012): one less container to run, and the ingest path
/// stays in-process and therefore easy to test.
/// </summary>
/// <remarks>
/// This is the only place that decides what a device may do on the wire, and it decides all four things
/// §02-design/06 §4 and §07-appendices/03 §3.1 require: a connection needs a credential that matches a
/// <c>DeviceCredential</c> row, plaintext is loopback-only while TLS carries the real traffic, a device may only
/// touch its own topic prefix, and revoking a device drops its live session (BR-05.4).
/// </remarks>
public sealed class MqttBrokerHostedService(
    MqttBrokerOptions options,
    MqttBrokerStatus status,
    DeviceSessionRegistry sessions,
    InProcessTelemetryBus ingestBus,
    IServiceScopeFactory scopes,
    ILogger<MqttBrokerHostedService> logger) : BackgroundService
{
    private MqttServer? _server;

    /// <summary>The one channel the broker forwards to the ingest worker (roadmap task 2.4).</summary>
    private const string TelemetryChannel = "telemetry";

    /// <summary>
    /// The host's shutdown token, kept so a publish that is waiting on a full ingest queue releases when the
    /// process is asked to stop. Read from the publish interceptor, which has no token of its own.
    /// </summary>
    private CancellationToken _stopping = CancellationToken.None;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;

        try
        {
            _server = new MqttFactory().CreateMqttServer(BuildOptions());

            _server.ValidatingConnectionAsync += OnValidatingConnectionAsync;
            _server.ClientConnectedAsync += OnClientConnectedAsync;
            _server.ClientDisconnectedAsync += OnClientDisconnectedAsync;
            _server.InterceptingPublishAsync += OnInterceptingPublishAsync;
            _server.InterceptingSubscriptionAsync += OnInterceptingSubscriptionAsync;

            // Revoking a device closes its live session through the registry. The delegate is installed before
            // the listener starts, so a revoke that races startup still finds it.
            sessions.UseDisconnect(DisconnectAsync);

            await _server.StartAsync();
            status.SetRunning(true);

            logger.LogInformation(
                "MQTT broker listening on {TlsHost}:{TlsPort} (tls: {Tls}) and {PlaintextHost}:{Port} (plaintext disabled: {PlaintextDisabled}); requireClientAuth: {RequireAuth}",
                options.Host,
                options.TlsPort,
                options.EnableTls,
                options.PlaintextHost,
                options.Port,
                options.DisablePlaintextEndpoint,
                options.RequireClientAuthentication);

            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            status.SetRunning(false);
            status.RecordError(ex.Message);

            // The API must still serve reads when the broker cannot start (degraded mode, NFR-03):
            // /health/ready reports the broker as unhealthy instead of crashing the process.
            logger.LogError(ex, "MQTT broker failed to start; the API continues in degraded mode");
        }
        finally
        {
            await StopAsync();
        }
    }

    /// <summary>
    /// Builds the two listeners. Plaintext binds <see cref="MqttBrokerOptions.PlaintextHost"/> (loopback unless a
    /// container overrides it) and can be switched off entirely; TLS is where a real device connects, and it is
    /// not started without a certificate.
    /// </summary>
    private MqttServerOptions BuildOptions()
    {
        var builder = new MqttServerOptionsBuilder();

        if (options.DisablePlaintextEndpoint)
        {
            builder.WithoutDefaultEndpoint();
        }
        else
        {
            builder
                .WithDefaultEndpoint()
                .WithDefaultEndpointBoundIPAddress(ParseAddress(options.PlaintextHost))
                .WithDefaultEndpointBoundIPV6Address(ParseAddressV6(options.PlaintextHost))
                .WithDefaultEndpointPort(options.Port);
        }

        if (options.EnableTls)
        {
            if (string.IsNullOrWhiteSpace(options.ServerCertificatePath) || !File.Exists(options.ServerCertificatePath))
            {
                // Not fatal: a local run without a certificate keeps the plaintext listener so the ingest path can
                // still be exercised, and /health/ready reports why TLS is missing (NFR-03 degraded mode).
                status.RecordError($"TLS is enabled but the certificate '{options.ServerCertificatePath}' was not found");
                logger.LogError(
                    "MQTT TLS is enabled but the certificate at {Path} does not exist; the TLS listener will not start",
                    options.ServerCertificatePath);
            }
            else
            {
                builder
                    .WithEncryptedEndpoint()
                    .WithEncryptedEndpointBoundIPAddress(ParseAddress(options.Host))
                    .WithEncryptedEndpointBoundIPV6Address(ParseAddressV6(options.Host))
                    .WithEncryptedEndpointPort(options.TlsPort)
                    .WithEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                        options.ServerCertificatePath,
                        options.ServerCertificatePassword))
                    .WithEncryptionSslProtocol(SslProtocols.Tls12);
            }
        }

        return builder.Build();
    }

    private static IPAddress ParseAddress(string host) =>
        IPAddress.TryParse(host, out var address) ? address : IPAddress.Any;

    /// <summary>
    /// The IPv6 counterpart of a bind address. MQTTnet opens an IPv6 socket as well, so binding only the IPv4
    /// address would leave the same port reachable over IPv6 — which for the plaintext listener would undo
    /// "loopback only" entirely. A loopback address maps to <c>::1</c>; anything else means "all interfaces".
    /// </summary>
    private static IPAddress ParseAddressV6(string host) =>
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)
            ? IPAddress.IPv6Loopback
            : IPAddress.IPv6Any;

    /// <summary>
    /// BR-05.1/BR-05.3: a connection is accepted only when its username names a device and its password matches
    /// one of that device's usable credentials. Everything else gets the same refusal, so a probe cannot tell a
    /// wrong secret from a device that does not exist.
    /// </summary>
    private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs context)
    {
        var deviceId = context.UserName;

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrEmpty(context.Password))
        {
            Refuse(context, "connection without a device credential");
            return;
        }

        if (!options.RequireClientAuthentication)
        {
            // Only reachable when authentication has been switched off on purpose (a local transport test).
            context.ReasonCode = MqttConnectReasonCode.Success;
            sessions.Connect(deviceId, context.ClientId);
            return;
        }

        DeviceCredentialVerification verification;

        // A scoped resolve per connection: the credential lookup needs the DbContext, and a singleton hosted
        // service must never hold one (captive dependency).
        await using (var scope = scopes.CreateAsyncScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<DeviceProvisioningService>();

            verification = await provisioning.VerifyCredentialAsync(deviceId, context.Password);
        }

        if (!verification.IsValid)
        {
            Refuse(context, $"invalid credential for device {deviceId}");
            return;
        }

        var open = sessions.Connect(deviceId, context.ClientId);

        logger.LogDebug(
            "MQTT device {DeviceId} authenticated on client {ClientId} ({Open} open session(s))",
            deviceId,
            context.ClientId,
            open);

        context.ReasonCode = MqttConnectReasonCode.Success;
    }

    private void Refuse(ValidatingConnectionEventArgs context, string reason)
    {
        context.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
        status.ConnectionRejected(reason);
        logger.LogWarning("Refused MQTT connection from client {ClientId}: {Reason}", context.ClientId, reason);
    }

    private Task OnClientConnectedAsync(ClientConnectedEventArgs args)
    {
        status.ClientConnected();
        logger.LogDebug("MQTT client connected: {ClientId}", args.ClientId);
        return Task.CompletedTask;
    }

    private Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs args)
    {
        status.ClientDisconnected();

        var deviceId = sessions.DeviceOf(args.ClientId);

        if (deviceId is not null)
        {
            sessions.Disconnect(deviceId, args.ClientId);
        }

        logger.LogDebug("MQTT client disconnected: {ClientId}", args.ClientId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Counts traffic and enforces the publish half of the ACL. A device may only publish under its own prefix, so
    /// a board cannot write into a terrarium it does not own; the refusal is also where a session held by a device
    /// that has since been revoked in the database is dropped (BR-05.4).
    /// </summary>
    private async Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
    {
        var deviceId = sessions.DeviceOf(args.ClientId);

        var decision = MqttTopicScheme.Authorize(
            args.ApplicationMessage.Topic,
            options.DeviceTopicPrefix,
            deviceId,
            TopicDirection.Publish);

        if (!decision.Allowed)
        {
            args.ProcessPublish = false;
            status.PublicationRefused();

            logger.LogWarning(
                "Refused publish to {Topic} from client {ClientId}: {Reason}",
                args.ApplicationMessage.Topic,
                args.ClientId,
                decision.Rejection);

            if (deviceId is not null)
            {
                await DisconnectAsync(deviceId, args.ClientId, CancellationToken.None).ConfigureAwait(false);
            }

            return;
        }

        status.MessagePublished();

        await ForwardToIngestAsync(args, deviceId).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands an accepted telemetry publication to the ingest worker.
    /// </summary>
    /// <remarks>
    /// Only <c>telemetry</c> is forwarded today: the health, status and events channels are part of the topic
    /// scheme but their consumers are later tasks (§02-design/02 §4.1 lifts a device out of <c>Offline</c> on a
    /// sample, and the <c>DeviceSilent</c>/<c>SensorFault</c> signals are 3.3). Leaving them unforwarded rather
    /// than half-handled is deliberate — a status payload accepted and then ignored would look like it worked.
    /// <para>
    /// The batch is awaited, not fired and forgotten: see <see cref="InProcessTelemetryBus"/> for why the
    /// acknowledgement has to wait behind the queue.
    /// </para>
    /// </remarks>
    private async Task ForwardToIngestAsync(InterceptingPublishEventArgs args, string? deviceId)
    {
        if (deviceId is null
            || !MqttTopicScheme.TryParse(args.ApplicationMessage.Topic, options.DeviceTopicPrefix, out var topic)
            || !string.Equals(topic.Channel, TelemetryChannel, StringComparison.Ordinal))
        {
            return;
        }

        await ingestBus.PublishAsync(
            new TelemetryEnvelope(
                topic.DeviceId,
                args.ApplicationMessage.PayloadSegment.ToArray(),
                DateTimeOffset.UtcNow,
                IngestSource.Mqtt),
            _stopping).ConfigureAwait(false);
    }

    /// <summary>The subscribe half of the ACL: a device subscribes to its own <c>cmd</c> topic and nothing else.</summary>
    private Task OnInterceptingSubscriptionAsync(InterceptingSubscriptionEventArgs args)
    {
        var deviceId = sessions.DeviceOf(args.ClientId);

        var decision = MqttTopicScheme.Authorize(
            args.TopicFilter.Topic,
            options.DeviceTopicPrefix,
            deviceId,
            TopicDirection.Subscribe);

        if (!decision.Allowed)
        {
            status.SubscriptionRefused();
            args.ProcessSubscription = false;
            args.Response.ReasonCode = MqttSubscribeReasonCode.TopicFilterInvalid;

            logger.LogWarning(
                "Refused subscription to {Topic} from client {ClientId}: {Reason}",
                args.TopicFilter.Topic,
                args.ClientId,
                decision.Rejection);
        }

        return Task.CompletedTask;
    }

    /// <summary>Closes one session. Called by the registry with (device id, client id).</summary>
    private async Task DisconnectAsync(string devicePublicId, string clientId, CancellationToken cancellationToken)
    {
        if (_server is null)
        {
            return;
        }

        await _server.DisconnectClientAsync(clientId, new MqttServerClientDisconnectOptions
        {
            ReasonCode = MqttDisconnectReasonCode.AdministrativeAction,
            ReasonString = string.IsNullOrEmpty(devicePublicId)
                ? "topic outside the device's own prefix"
                : "device revoked or credential rejected",
        }).ConfigureAwait(false);

        status.SessionKicked();

        logger.LogInformation(
            "Disconnected MQTT session {ClientId} of device {DeviceId}",
            clientId,
            devicePublicId);
    }

    private async Task StopAsync()
    {
        if (_server is null)
        {
            return;
        }

        try
        {
            await _server.StopAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while stopping the MQTT broker");
        }
        finally
        {
            status.SetRunning(false);
            _server.Dispose();
            _server = null;
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
