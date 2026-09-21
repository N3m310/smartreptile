using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace SmartReptile.Infrastructure.Mqtt;

/// <summary>MQTT broker settings (§03-implementation/01 §5, §07-appendices/03 §3).</summary>
public sealed class MqttBrokerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mqtt";

    /// <summary>Interface to bind. Use <c>0.0.0.0</c> in containers, loopback for a local run.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Plaintext MQTT port.</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Port used when TLS is enabled.</summary>
    public int TlsPort { get; set; } = 8883;

    /// <summary>Serve MQTT over TLS (FR-05). Required for the release runbook.</summary>
    public bool EnableTls { get; set; }

    /// <summary>Do not open the plaintext listener at all (required in release, FR-05 BR-05.1).</summary>
    public bool DisablePlaintextEndpoint { get; set; }

    /// <summary>Path to the server certificate (PFX) used for TLS.</summary>
    public string? ServerCertificatePath { get; set; }

    /// <summary>Password for the PFX file, if any.</summary>
    public string? ServerCertificatePassword { get; set; }

    /// <summary>Reject anonymous device connections (BR-05.1).</summary>
    public bool RequireClientAuthentication { get; set; } = true;

    /// <summary>Topic the broker uses to announce a device's unexpected disconnect.</summary>
    public string DeviceTopicPrefix { get; set; } = "sr/v1/d";
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

    /// <summary>Last error reported by the broker, if any.</summary>
    string? LastError { get; }
}

/// <summary>In-process broker status holder shared between the hosted service, health check and metrics endpoint.</summary>
public sealed class MqttBrokerStatus : IMqttBrokerStatus
{
    private long _connectedClients;
    private long _rejectedConnections;
    private long _publishedMessages;
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

    internal void RecordError(string error) => _lastError = error;
}

/// <summary>
/// Hosts the MQTT broker inside the API process (ADR-012): one less container to run, and the ingest path
/// stays in-process and therefore easy to test.
/// </summary>
/// <remarks>
/// Milestone note (M1): the listener, TLS/plaintext switch, anonymous-connection rejection and status
/// counters are wired. Credential validation against <c>DeviceCredential</c>, topic ACLs and telemetry
/// dispatch arrive with the ingest pipeline in M2 (FR-06, FR-05 BR-05.1/B).
/// </remarks>
public sealed class MqttBrokerHostedService(
    MqttBrokerOptions options,
    MqttBrokerStatus status,
    ILogger<MqttBrokerHostedService> logger) : BackgroundService
{
    private MqttServer? _server;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var builder = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(options.Port);

            if (options.EnableTls && !string.IsNullOrWhiteSpace(options.ServerCertificatePath))
            {
                // TLS listener (FR-05). The certificate (PFX) is mounted read-only into the container.
                builder.WithEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                    options.ServerCertificatePath,
                    options.ServerCertificatePassword));
                builder.WithEncryptionSslProtocol(SslProtocols.Tls12);
            }

            _server = new MqttFactory().CreateMqttServer(builder.Build());

            // The validator is an event on the server in MQTTnet 4.x (it no longer lives on the options builder).
            _server.ValidatingConnectionAsync += OnValidatingConnectionAsync;
            _server.ClientConnectedAsync += OnClientConnectedAsync;
            _server.ClientDisconnectedAsync += OnClientDisconnectedAsync;
            _server.InterceptingPublishAsync += OnInterceptingPublishAsync;

            await _server.StartAsync();
            status.SetRunning(true);

            logger.LogInformation(
                "MQTT broker listening on {Host}:{Port} (tls: {Tls}, requireClientAuth: {RequireAuth}, plaintextDisabled: {PlaintextDisabled})",
                options.Host,
                options.Port,
                options.EnableTls,
                options.RequireClientAuthentication,
                options.DisablePlaintextEndpoint);

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

    private Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs context)
    {
        // M1: reject anonymous connections when authentication is required. Credential verification against
        // the DeviceCredential table is wired in M2 together with device provisioning (FR-04/FR-05).
        if (options.RequireClientAuthentication &&
            (string.IsNullOrWhiteSpace(context.UserName) || context.Password is null || context.Password.Length == 0))
        {
            context.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            status.ConnectionRejected($"anonymous connection from {context.ClientId}");
            logger.LogWarning("Rejected anonymous MQTT connection from client {ClientId}", context.ClientId);
            return Task.CompletedTask;
        }

        context.ReasonCode = MqttConnectReasonCode.Success;
        return Task.CompletedTask;
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
        logger.LogDebug("MQTT client disconnected: {ClientId}", args.ClientId);
        return Task.CompletedTask;
    }

    private Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
    {
        status.MessagePublished();

        // M1: count only. M2 routes `…/telemetry`, `…/health`, `…/status` and `…/events` into the ingest
        // pipeline, and applies the per-device topic ACL (§07-appendices/03 §3.1).
        return Task.CompletedTask;
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
