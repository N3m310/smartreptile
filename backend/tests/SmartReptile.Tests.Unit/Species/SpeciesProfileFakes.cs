using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Species;

/// <summary>
/// In-memory <see cref="ISpeciesProfileStore"/>. Same rule as the other fakes in this suite: no database, no clock,
/// and the row builders take explicit arguments so a test says what it means.
/// </summary>
/// <remarks>
/// Two behaviours mirror the real adapter rather than being simpler than it, because tests rely on them:
/// <see cref="ListVisibleAsync"/> filters ownership the way the query does (a foreign custom profile is absent, not
/// hidden afterwards), and <see cref="NameTakenAsync"/> compares the way the column's collation does — case
/// insensitively.
/// </remarks>
internal sealed class FakeSpeciesProfileStore : ISpeciesProfileStore
{
    private readonly List<SpeciesProfile> _profiles = [];
    private readonly List<AuditLog> _audit = [];
    private readonly HashSet<Guid> _users = [];
    private readonly HashSet<Guid> _assigned = [];

    /// <summary>How many times the store was committed — asserted so "nothing was written" is a real assertion.</summary>
    public int SaveCount { get; private set; }

    public IReadOnlyList<SpeciesProfile> Profiles => _profiles;

    /// <summary>Audit rows staged on this unit of work, in the order they were added.</summary>
    public IReadOnlyList<AuditLog> AuditEntries => _audit;

    /// <summary>Registers an account, so the write paths' caller check accepts it.</summary>
    public void AddUser(Guid userId) => _users.Add(userId);

    /// <summary>Marks a profile as assigned to a terrarium, which is what the delete rule refuses.</summary>
    public void AssignToTerrarium(Guid profileId) => _assigned.Add(profileId);

    /// <summary>A seeded profile: no owner, immutable, with the bands a test names.</summary>
    public SpeciesProfile AddBuiltIn(string name, params (MetricCode Metric, decimal Min, decimal Max)[] bands)
    {
        var profile = new SpeciesProfile
        {
            Name = name,
            IsBuiltIn = true,
            CreatedByUserId = null,
        };

        foreach (var (metric, min, max) in bands)
        {
            profile.Thresholds.Add(new Threshold
            {
                Metric = metric,
                Phase = ThresholdPhase.Any,
                TargetMin = min,
                TargetMax = max,
                SourceRef = "test citation",
            });
        }

        _profiles.Add(profile);
        return profile;
    }

    /// <summary>Stages a row the way a test story describes it, without going through the service.</summary>
    public SpeciesProfile AddProfile(Guid ownerId, string name, params (MetricCode Metric, decimal Min, decimal Max)[] bands)
    {
        var profile = new SpeciesProfile
        {
            Name = name,
            IsBuiltIn = false,
            CreatedByUserId = ownerId,
        };

        foreach (var (metric, min, max) in bands)
        {
            profile.Thresholds.Add(new Threshold { Metric = metric, TargetMin = min, TargetMax = max });
        }

        _profiles.Add(profile);
        return profile;
    }

    public Task<IReadOnlyList<SpeciesProfile>> ListVisibleAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpeciesProfile>>(
            [.. _profiles
                .Where(profile => profile.IsBuiltIn || profile.CreatedByUserId == userId)
                .OrderBy(profile => profile.Name, StringComparer.Ordinal)]);

    public Task<SpeciesProfile?> FindAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.FirstOrDefault(profile => profile.Id == profileId));

    public Task<bool> NameTakenAsync(Guid userId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.Any(profile =>
            profile.CreatedByUserId == userId
            && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> IsAssignedAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult(_assigned.Contains(profileId));

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Contains(userId));

    public void Add(SpeciesProfile profile) => _profiles.Add(profile);

    public void ReplaceBands(SpeciesProfile profile, IReadOnlyList<Threshold> bands)
    {
        profile.Thresholds.Clear();

        foreach (var band in bands)
        {
            band.SpeciesProfileId = profile.Id;
            profile.Thresholds.Add(band);
        }
    }

    public void Remove(SpeciesProfile profile) => _profiles.Remove(profile);

    public void AddAuditEntry(AuditLog entry) => _audit.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.CompletedTask;
    }
}
