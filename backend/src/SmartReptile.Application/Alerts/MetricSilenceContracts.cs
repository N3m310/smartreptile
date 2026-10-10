using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Application.Alerts;

/// <summary>Body of <c>POST /terrariums/{id}/silences</c> (FR-13, `07-appendices/03` §4.2).</summary>
/// <param name="Metric">Metric key to silence, or null/absent for every metric of the terrarium.</param>
/// <param name="UntilUtc">Instant the silence ends; must be within the next <see cref="MetricSilence.MaxWindowHours"/> hours.</param>
/// <param name="Reason">Why the keeper silenced it. Required.</param>
public sealed record CreateSilenceRequest(string? Metric, DateTimeOffset? UntilUtc, string? Reason);

/// <summary>Body of <c>POST /alerts/{id}/resolve</c> (FR-12, `07-appendices/03` §4.5).</summary>
/// <param name="Reason">One of <c>Recovered</c>, <c>FalsePositive</c>, <c>SensorFault</c>, <c>Accepted</c>.</param>
/// <param name="Note">What the keeper wrote, when they wrote something. Stored on the audit entry.</param>
public sealed record ResolveAlertRequest(string? Reason, string? Note);

/// <summary>One silence window as the API returns it.</summary>
/// <param name="Id">Identity, used by the cancel route.</param>
/// <param name="TerrariumId">Terrarium it suppresses.</param>
/// <param name="Metric">Metric suppressed as the REST key, or null when every metric is.</param>
/// <param name="UntilUtc">Instant it ends.</param>
/// <param name="Reason">Why the keeper silenced it.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="CreatedByUserId">Who created it.</param>
/// <param name="Active">True while it still suppresses something.</param>
public sealed record MetricSilenceView(
    Guid Id,
    Guid TerrariumId,
    string? Metric,
    DateTimeOffset UntilUtc,
    string Reason,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    bool Active);

/// <summary>
/// An expected failure of a silence use case. Field-level violations reuse <see cref="IdentityViolation"/>, which
/// already means "a named field was refused, with a stable code".
/// </summary>
/// <param name="Code">Stable problem code, e.g. <c>not_found</c>.</param>
/// <param name="Message">Message safe to show the user.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
public sealed record SilenceProblem(
    string Code,
    string Message,
    IReadOnlyList<IdentityViolation>? Errors = null);

/// <summary>
/// Outcome of a silence use case. At most one payload is set; a null <see cref="Problem"/> means success.
/// </summary>
public sealed record SilenceOutcome
{
    /// <summary>Set by list.</summary>
    public IReadOnlyList<MetricSilenceView>? Silences { get; init; }

    /// <summary>Set by create and cancel.</summary>
    public MetricSilenceView? Silence { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public SilenceProblem? Problem { get; init; }

    /// <summary>True when there is no problem.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A failure the caller caused or should see.</summary>
    /// <param name="problem">What went wrong.</param>
    public static SilenceOutcome Failed(SilenceProblem problem) => new() { Problem = problem };

    /// <summary>
    /// The terrarium is not the caller's, or does not exist — one answer for both, so the routes cannot be used to
    /// enumerate ids (BR-02.2).
    /// </summary>
    public static SilenceOutcome NotFound() => Failed(new SilenceProblem("not_found", "No such terrarium."));

    /// <summary>A silence id that is not this terrarium's, which is also what a made-up id gets.</summary>
    public static SilenceOutcome SilenceNotFound() => Failed(new SilenceProblem("not_found", "No such silence."));
}

/// <summary>
/// Persistence for the silence windows of FR-13 (roadmap task 3.4).
/// </summary>
/// <remarks>
/// Separate from <see cref="IAlertStore"/> because the two are separate features that happen to share a screen: an
/// alert can be acted on with no silence in sight, and a silence outlives every alert it suppresses.
/// </remarks>
public interface IMetricSilenceStore
{
    /// <summary>True when <paramref name="terrariumId"/> exists, is active and belongs to <paramref name="userId"/>.</summary>
    /// <param name="terrariumId">Terrarium to check.</param>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsMemberAsync(Guid terrariumId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Silences of one terrarium that are still suppressing at <paramref name="atUtc"/>, newest first.</summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="atUtc">Instant to judge "still suppressing" at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<MetricSilence>> ListActiveAsync(
        Guid terrariumId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>One silence of one terrarium, whatever its state; null when it is not there.</summary>
    /// <param name="terrariumId">Terrarium the silence must belong to.</param>
    /// <param name="silenceId">Silence identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MetricSilence?> FindAsync(Guid terrariumId, Guid silenceId, CancellationToken cancellationToken);

    /// <summary>Stages a new silence window.</summary>
    /// <param name="silence">Row to stage.</param>
    void Add(MetricSilence silence);

    /// <summary>Stages an audit row. It commits with the change it describes, never on its own.</summary>
    /// <param name="entry">Row to stage.</param>
    void AddAuditEntry(AuditLog entry);

    /// <summary>Commits everything this scope staged.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
