using SmartReptile.Domain.Alerts;

namespace SmartReptile.Domain.Devices;

/// <summary>
/// The sensor-fault rule (FR-07 / BR-07.1; §02-design/03 §4.3, §02-design/05 §2): a probe that has stopped
/// answering is a *monitoring* failure, and the system has to say so instead of letting the gap read as a habitat
/// that is fine.
/// </summary>
/// <remarks>
/// The device does the counting — BR-07.1 has the firmware raise a <c>sensor_fault</c> event once three
/// consecutive reads have failed, and the payload carries that count — so the server's check is a guard rather
/// than a counter. It exists so that an early warning (a firmware reporting one or two failures) does not mark a
/// working metric unavailable, and an event carrying no count at all is taken at its word, because the device only
/// raises this event after its own threshold.
/// <para>
/// The severity is Warning rather than Info. §02-design/05 §1 reserves Info for timeline entries that ask nothing
/// of the keeper; losing the ability to watch a probe is not one of those, and it is exactly the case where a
/// quiet timeline would be the wrong answer.
/// </para>
/// </remarks>
public static class SensorFaultPolicy
{
    /// <summary>Consecutive read failures the device's own rule waits for before reporting a fault (BR-07.1).</summary>
    public const int MinConsecutiveFailures = 3;

    /// <summary>The severity a confirmed fault is raised at.</summary>
    public const AlertSeverity Severity = AlertSeverity.Warning;

    /// <summary>
    /// True when a <c>sensor_fault</c> payload is the confirmed failure the design describes: it reports at least
    /// <see cref="MinConsecutiveFailures"/> consecutive failures, or reports no count at all.
    /// </summary>
    /// <param name="consecutiveFailures">Failures the payload reported, when it reported any.</param>
    public static bool IsConfirmed(int? consecutiveFailures) =>
        consecutiveFailures is null or >= MinConsecutiveFailures;
}
