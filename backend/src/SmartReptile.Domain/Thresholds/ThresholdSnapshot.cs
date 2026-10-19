namespace SmartReptile.Domain.Thresholds;

/// <summary>
/// The effective bands of one terrarium captured at one instant (§02-design/02 §3.13).
/// </summary>
/// <remarks>
/// Written by the threshold change path (roadmap 3.1's write half) whenever an override is authored or reverted, so
/// a later edit does not silently rewrite history: an alert or a rollup can still say which bands it was judged
/// against after the keeper has moved the limit (UC-03 A4, R-08). The row snapshots the <i>resolved</i> set rather
/// than the overrides, because the question it answers — "what was in force?" — depends on the profile too.
/// <see cref="ThresholdVersionHash"/> is that same content hashed, so two snapshots can be compared, or an alert
/// linked to the version it used, without parsing the JSON.
/// </remarks>
public class ThresholdSnapshot
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Terrarium the bands belong to.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Instant the effective set was resolved at, in UTC.</summary>
    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>
    /// The effective set as JSON, in the shape <c>GET /terrariums/{id}/thresholds</c> returns. One entry per metric
    /// that resolved at <see cref="CapturedAtUtc"/>, ordered by metric key.
    /// </summary>
    public string EffectiveThresholdsJson { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="EffectiveThresholdsJson"/>, lower-case hex (64 characters).</summary>
    public string ThresholdVersionHash { get; set; } = string.Empty;
}
