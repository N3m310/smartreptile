namespace SmartReptile.Domain.Common;

/// <summary>
/// Raised when an operation would violate a domain rule (band ordering, plausible ranges, …).
/// Expected failures that should reach the client as <c>400</c> are modelled as validation results
/// instead (see <see cref="Thresholds.ThresholdBand.Validate"/>); this exception is for programming
/// errors and invariant violations (§03-implementation/02 §2.3).
/// </summary>
public sealed class DomainValidationException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code exposed in the RFC 7807 problem body.</summary>
    public string Code { get; } = code;
}
