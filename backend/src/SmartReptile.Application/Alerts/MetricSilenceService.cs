using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Alerts;

/// <summary>
/// The silence windows of FR-13 (roadmap task 3.4): creating, listing and cancelling them.
/// </summary>
/// <remarks>
/// <b>What a silence does and does not do.</b> It suppresses <em>notification</em>, not detection: the evaluator
/// keeps raising, touching and resolving alerts during a silence, the rows keep counting, and the dashboard keeps
/// showing them. §02-design/05 §4 puts the silence check inside the notification decision flow, and its
/// <c>silenced_metric</c> suppression reason is what the report will count — so this service is the write side of a
/// rule whose read side the dispatcher of task 3.5 performs with the same
/// <see cref="MetricSilence.IsActiveAt"/> and <see cref="MetricSilence.Covers"/> this file uses.
/// <para>
/// <b>Why every silence is audited and none is silent.</b> The design's sentence is "deliberate, accountable
/// suppression": the row carries who asked and why, the audit entry carries where they asked from, and both are
/// visible on the dashboard while the window is open. A suppression nobody can see is indistinguishable from a
/// broken notifier.
/// </para>
/// </remarks>
public sealed class MetricSilenceService(IMetricSilenceStore store, IClock clock)
{
    /// <summary>Silences of one terrarium that are still suppressing.</summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SilenceOutcome> ListAsync(
        Guid terrariumId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await store.IsMemberAsync(terrariumId, userId, cancellationToken).ConfigureAwait(false))
        {
            return SilenceOutcome.NotFound();
        }

        var silences = await store
            .ListActiveAsync(terrariumId, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return new SilenceOutcome { Silences = silences.Select(silence => Describe(silence, clock.UtcNow)).ToArray() };
    }

    /// <summary>Creates a window, once the rules of <see cref="MetricSilencePolicy"/> allow it.</summary>
    /// <param name="terrariumId">Terrarium to suppress.</param>
    /// <param name="userId">Signed-in account; also the creator recorded.</param>
    /// <param name="metric">Metric to silence, or null for every metric.</param>
    /// <param name="request">Window and reason as the caller sent them.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SilenceOutcome> CreateAsync(
        Guid terrariumId,
        Guid userId,
        MetricCode? metric,
        CreateSilenceRequest request,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await store.IsMemberAsync(terrariumId, userId, cancellationToken).ConfigureAwait(false))
        {
            return SilenceOutcome.NotFound();
        }

        if (request.UntilUtc is not { } untilUtc)
        {
            return Invalid("untilUtc", "silence_until_required", "untilUtc is required.");
        }

        var now = clock.UtcNow;

        if (RefusalFor(now, untilUtc, request.Reason) is { } violation)
        {
            return SilenceOutcome.Failed(new SilenceProblem(
                "validation_failed",
                violation.Message,
                [violation]));
        }

        var silence = new MetricSilence
        {
            TerrariumId = terrariumId,
            Metric = metric,
            UntilUtc = untilUtc,
            Reason = request.Reason!.Trim(),
            CreatedByUserId = userId,
            CreatedAt = now,
        };

        store.Add(silence);

        // The audit row commits with the window it describes, so a silence cannot exist without a record of who
        // asked for it. The before/after pair is the window itself, which is what an auditor wants to see.
        store.AddAuditEntry(AuditLog.ForSilence(
            AuditAction.MetricSilenced,
            silence.Id,
            userId,
            actor,
            now,
            afterJson: JsonSerializer.Serialize(new
            {
                terrariumId,
                metric = metric?.ToString(),
                untilUtc,
                reason = silence.Reason,
            })));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SilenceOutcome { Silence = Describe(silence, now) };
    }

    /// <summary>
    /// Cancels a window early.
    /// </summary>
    /// <remarks>
    /// Idempotent: cancelling a window that is already cancelled changes nothing, writes no second audit row and
    /// still answers success. A <c>DELETE</c> that fails the second time it is retried would make a client believe
    /// the silence is still in force, which is the one reading that could hide alerts from a keeper.
    /// </remarks>
    /// <param name="terrariumId">Terrarium the silence belongs to.</param>
    /// <param name="silenceId">Silence identity.</param>
    /// <param name="userId">Signed-in account; also the actor recorded.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SilenceOutcome> CancelAsync(
        Guid terrariumId,
        Guid silenceId,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        if (!await store.IsMemberAsync(terrariumId, userId, cancellationToken).ConfigureAwait(false))
        {
            return SilenceOutcome.NotFound();
        }

        var silence = await store.FindAsync(terrariumId, silenceId, cancellationToken).ConfigureAwait(false);

        if (silence is null)
        {
            return SilenceOutcome.SilenceNotFound();
        }

        var now = clock.UtcNow;

        if (silence.CancelledAt is null)
        {
            silence.CancelledAt = now;

            store.AddAuditEntry(AuditLog.ForSilence(
                AuditAction.SilenceCancelled,
                silence.Id,
                userId,
                actor,
                now,
                beforeJson: JsonSerializer.Serialize(new { untilUtc = silence.UntilUtc })));

            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SilenceOutcome { Silence = Describe(silence, now) };
    }

    /// <summary>
    /// The field violation behind a refused window, or null when the request is acceptable.
    /// </summary>
    /// <remarks>
    /// The rule itself is <see cref="MetricSilencePolicy"/>'s; what happens here is only its translation into the
    /// field-level vocabulary every other validation failure in this API uses, so the clients render one shape.
    /// </remarks>
    private static IdentityViolation? RefusalFor(DateTimeOffset nowUtc, DateTimeOffset untilUtc, string? reason) =>
        MetricSilencePolicy.Validate(nowUtc, untilUtc, reason) switch
        {
            SilenceRefusal.ReasonMissing => new IdentityViolation(
                "reason", "silence_reason_required", "A reason is required."),
            SilenceRefusal.ReasonTooLong => new IdentityViolation(
                "reason",
                "silence_reason_too_long",
                $"A reason may be at most {MetricSilence.ReasonMaxLength} characters."),
            SilenceRefusal.UntilNotInTheFuture => new IdentityViolation(
                "untilUtc", "silence_until_not_in_the_future", "untilUtc must be in the future."),
            SilenceRefusal.WindowTooLong => new IdentityViolation(
                "untilUtc",
                "silence_window_too_long",
                $"A silence may last at most {MetricSilence.MaxWindowHours} hours."),
            _ => null,
        };

    private static SilenceOutcome Invalid(string field, string code, string message) =>
        SilenceOutcome.Failed(new SilenceProblem(
            "validation_failed",
            message,
            [new IdentityViolation(field, code, message)]));

    /// <summary>Projects a stored window onto the response shape.</summary>
    private static MetricSilenceView Describe(MetricSilence silence, DateTimeOffset nowUtc) => new(
        silence.Id,
        silence.TerrariumId,
        silence.Metric is { } metric ? MetricDictionary.Get(metric).ApiKey : null,
        silence.UntilUtc,
        silence.Reason,
        silence.CreatedAt,
        silence.CreatedByUserId,
        silence.IsActiveAt(nowUtc));
}
