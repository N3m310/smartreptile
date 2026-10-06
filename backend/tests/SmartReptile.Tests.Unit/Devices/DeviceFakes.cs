using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Terrariums;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// Fakes for the onboarding ports. Same rule as the auth fakes: no database, no sockets, no real crypto — and the
/// claim-code generator must stay inside the configured alphabet, because the claim path shape-checks the code
/// before it looks it up.
/// </summary>
internal sealed class FakeClaimCodeGenerator : IClaimCodeGenerator
{
    private int _issued;

    public const string Alphabet = ClaimCode.DefaultAlphabet;

    public string Generate()
    {
        var sequence = Interlocked.Increment(ref _issued);
        var code = new char[ClaimCode.DefaultLength];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[(sequence + index) % Alphabet.Length];
        }

        return new string(code);
    }
}

/// <summary>Deterministic secret material, so a test can name the secret it expects.</summary>
internal sealed class FakeDeviceCredentials : IDeviceCredentials
{
    private int _secrets;
    private int _ids;

    public string NewSecret() => $"device-secret-{Interlocked.Increment(ref _secrets)}";

    public string NewPublicId() => $"sr-{Interlocked.Increment(ref _ids):D6}";

    public DeviceSecretHash HashSecret(string secret)
    {
        var salt = new byte[16];

        return new DeviceSecretHash(Digest(secret, salt), salt);
    }

    public bool VerifySecret(string secret, byte[] hash, byte[] salt) =>
        hash.Length > 0 && salt.Length > 0 && Digest(secret, salt).SequenceEqual(hash);

    private static byte[] Digest(string secret, byte[] salt) =>
        SHA256.HashData([.. Encoding.UTF8.GetBytes(secret), .. salt]);
}

/// <summary>In-memory onboarding counters with the same two windows as the real store.</summary>
internal sealed class FakeOnboardingThrottleStore : IOnboardingThrottleStore
{
    private readonly List<(string Address, DateTimeOffset At)> _attempts = [];

    public OnboardingAttemptWindow GetWindow(string ipAddress, DateTimeOffset sinceIpUtc, DateTimeOffset sinceGlobalUtc) =>
        new(
            _attempts.Count(attempt => attempt.Address == ipAddress && attempt.At >= sinceIpUtc),
            _attempts.Count(attempt => attempt.At >= sinceGlobalUtc));

    public void RecordAttempt(string ipAddress, DateTimeOffset nowUtc) => _attempts.Add((ipAddress, nowUtc));
}

/// <summary>
/// Records the sessions a device holds and the kicks it was asked for, so a revoke can be asserted without a
/// broker. Mirrors the real registry's contract: a kick closes everything the device has and returns the count.
/// </summary>
internal sealed class FakeDeviceSessionRegistry : IDeviceSessionRegistry
{
    private readonly Dictionary<string, HashSet<string>> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public List<(string DeviceId, string Reason)> Kicks { get; } = [];

    public int ConnectedDevices => _sessions.Count;

    public void Connect(string devicePublicId, params string[] clientIds)
    {
        if (!_sessions.TryGetValue(devicePublicId, out var clients))
        {
            clients = new HashSet<string>(StringComparer.Ordinal);
            _sessions[devicePublicId] = clients;
        }

        foreach (var clientId in clientIds)
        {
            clients.Add(clientId);
        }
    }

    public bool IsConnected(string devicePublicId) =>
        _sessions.TryGetValue(devicePublicId, out var clients) && clients.Count > 0;

    public Task<int> KickAsync(string devicePublicId, string reason, CancellationToken cancellationToken = default)
    {
        Kicks.Add((devicePublicId, reason));

        if (!_sessions.Remove(devicePublicId, out var clients))
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(clients.Count);
    }
}

/// <summary>
/// In-memory <see cref="IProvisioningStore"/>. Returns the same instances the real context would track, and fixes
/// up the credential navigation the way EF Core does on save, so verification sees what a claim wrote.
/// </summary>
internal sealed class FakeProvisioningStore : IProvisioningStore
{
    private readonly List<Device> _devices = [];
    private readonly List<Terrarium> _terrariums = [];

    public IReadOnlyList<Device> Devices => _devices;

    public IReadOnlyList<Terrarium> Terrariums => _terrariums;

    public int SaveCount { get; private set; }

    /// <summary>Adds a terrarium row directly, standing in for the not-yet-built terrarium endpoints (task 2.8).</summary>
    public Terrarium AddTerrarium(Guid ownerUserId, string name = "Test box")
    {
        var terrarium = new Terrarium { UserId = ownerUserId, Name = name };
        _terrariums.Add(terrarium);
        return terrarium;
    }

    public Task<Device?> FindDeviceByPublicIdAsync(string publicId, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.FirstOrDefault(device => device.PublicId == publicId));

    public Task<Device?> FindDeviceByChipIdAsync(string chipId, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.FirstOrDefault(device => device.ChipId == chipId));

    public Task<Device?> FindDeviceByClaimCodeAsync(string claimCode, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.FirstOrDefault(device => device.ClaimCode == claimCode));

    public Task<bool> PublicIdExistsAsync(string publicId, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.Any(device => device.PublicId == publicId));

    public void AddDevice(Device device) => _devices.Add(device);

    public void AddCredential(DeviceCredential credential)
    {
        var device = _devices.FirstOrDefault(candidate => candidate.Id == credential.DeviceId);

        credential.Device = device;
        device?.Credentials.Add(credential);
    }

    public Task<Terrarium?> FindOwnedTerrariumAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_terrariums.FirstOrDefault(
            terrarium => terrarium.Id == terrariumId && terrarium.UserId == ownerUserId && terrarium.DeletedAt == null));

    public Task<bool> TerrariumHasDeviceAsync(Guid terrariumId, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.Any(
            device => device.TerrariumId == terrariumId && device.Status != DeviceStatus.Revoked));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
