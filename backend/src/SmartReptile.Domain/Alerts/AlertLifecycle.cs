namespace SmartReptile.Domain.Alerts;

/// <summary>
/// The two things a human may do to an alert (FR-12, roadmap task 3.4).
/// </summary>
public enum AlertTransition
{
    /// <summary>Take ownership of an episode without closing it.</summary>
    Acknowledge = 0,

    /// <summary>Close an episode, with a reason.</summary>
    Resolve = 1,
}

/// <summary>
/// The acknowledgement and resolution rules of §02-design/02 §4.2, as pure transitions over one
/// <see cref="Alert"/>.
/// </summary>
/// <remarks>
/// <b>Why the rules are here and not in the service.</b> They are the part of the lifecycle a client can get
/// wrong, they are stated once in the contract, and they are cheap to test without a database (`TC-I-07`'s
/// "a second acknowledgement is refused" is a fact about a row, not about HTTP).
/// <para>
/// <b>Four readings settled here.</b> First, only an <see cref="AlertState.Open"/> alert may be acknowledged, and a
/// second acknowledgement is refused with the same answer as a resolved one: both mean "your request did not change
/// anything", and one code is one thing for a client to handle. Second, resolution is allowed straight from
/// <see cref="AlertState.Open"/> — a keeper who sees the alert and knows the answer does not have to acknowledge it
/// first — and it deliberately does <b>not</b> fabricate an acknowledgement: the timeline should say what happened,
/// and what happened is a resolve. Third, a resolved alert is terminal, so every further transition is refused
/// rather than applied twice (the row keeps its first reason and its first actor). Fourth, nothing here touches
/// severity: escalation is the evaluator's (3.2), and a human lowering a severity would be editing the record of
/// what the biology did.
/// </para>
/// </remarks>
public static class AlertLifecycle
{
    /// <summary>True when <paramref name="alert"/> is in a state that allows <paramref name="transition"/>.</summary>
    /// <param name="alert">Alert as stored.</param>
    /// <param name="transition">What the caller asked for.</param>
    public static bool Allows(Alert alert, AlertTransition transition)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return transition switch
        {
            AlertTransition.Acknowledge => alert.State == AlertState.Open,
            AlertTransition.Resolve => alert.State != AlertState.Resolved,
            _ => false,
        };
    }

    /// <summary>
    /// Records who took ownership of the episode, when the row allows it.
    /// </summary>
    /// <param name="alert">Alert to mutate.</param>
    /// <param name="userId">Acting user.</param>
    /// <param name="at">Instant of the request.</param>
    /// <returns>True when the transition was applied; false when the alert was not open.</returns>
    public static bool Acknowledge(Alert alert, Guid userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!Allows(alert, AlertTransition.Acknowledge))
        {
            return false;
        }

        alert.State = AlertState.Acknowledged;
        alert.AcknowledgedAt = at;
        alert.AcknowledgedByUserId = userId;

        return true;
    }

    /// <summary>
    /// Closes the episode with the reason the keeper gave, when the row allows it.
    /// </summary>
    /// <param name="alert">Alert to mutate.</param>
    /// <param name="userId">Acting user.</param>
    /// <param name="reason">Why it was closed.</param>
    /// <param name="at">Instant of the request.</param>
    /// <returns>True when the transition was applied; false when the alert was already resolved.</returns>
    public static bool Resolve(Alert alert, Guid userId, ResolvedReason reason, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!Allows(alert, AlertTransition.Resolve))
        {
            return false;
        }

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = at;
        alert.ResolvedByUserId = userId;
        alert.ResolvedReason = reason;

        return true;
    }

    /// <summary>
    /// True when closing this alert has to re-arm a dwell key as well.
    /// </summary>
    /// <remarks>
    /// A threshold alert names the <c>(terrarium, metric, phase)</c> key whose <see cref="Evaluation.EvaluationState"/>
    /// is still holding the excursion's start. Without re-arming it, the next out-of-band reading of a metric that
    /// never came back into its band would open a brand-new alert <b>instantly</b> — the dwell window has already
    /// expired — so a keeper who resolved an alert as <see cref="ResolvedReason.Accepted"/> would be told the same
    /// thing again one sample later. Re-arming means the alert may only return after the band has been left for the
    /// dwell once more; a keeper who wants a *longer* quiet period uses a metric silence, which is time-boxed,
    /// reason-carrying and visible (§02-design/05 §5). The device-level sources
    /// (<see cref="AlertSource.DeviceSilent"/>, <see cref="AlertSource.SensorFault"/>,
    /// <see cref="AlertSource.DeviceClockSkew"/>) have no such key — they are watched by their own sweeps — so there
    /// is nothing to re-arm.
    /// </remarks>
    /// <param name="alert">Alert that was just resolved.</param>
    public static bool RearmsItsKey(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return alert.Source == AlertSource.Threshold && alert.Metric is not null;
    }
}
