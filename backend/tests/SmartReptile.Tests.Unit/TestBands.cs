using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit;

/// <summary>
/// The bands the decision tests drive the engine with.
/// </summary>
/// <remarks>
/// One helper rather than a literal per test, because the numbers are the ones the design documents: the leopard
/// gecko band (`07-appendices/05` §3) is what `03-implementation/06` §3's worked examples and every `TC-U-10…20`
/// scenario are written against, so a test that changes a bound has to say so out loud.
/// </remarks>
internal static class TestBands
{
    /// <summary>Seeded leopard gecko day band: 26–32 °C, critical 22–34.5 °C, dwell 5/2, recovery 0.5 °C.</summary>
    public static ThresholdBand LeopardGecko(ThresholdPhase phase = ThresholdPhase.Any) => new()
    {
        Metric = MetricCode.TempC,
        Phase = phase,
        TargetMin = 26m,
        TargetMax = 32m,
        CriticalMin = 22m,
        CriticalMax = 34.5m,
        DwellWarnMinutes = 5,
        DwellCritMinutes = 2,
        RecoveryMargin = 0.5m,
        Enabled = true,
    };

    /// <summary>
    /// A band whose dwell windows are one minute, so a documented multi-step episode can be written as minutes
    /// rather than as an hour of simulated samples.
    /// </summary>
    public static ThresholdBand FastDwell(decimal targetMin = 26m, decimal targetMax = 32m) => new()
    {
        Metric = MetricCode.TempC,
        Phase = ThresholdPhase.Any,
        TargetMin = targetMin,
        TargetMax = targetMax,
        CriticalMin = targetMin - 4m,
        CriticalMax = targetMax + 2.5m,
        DwellWarnMinutes = 1,
        DwellCritMinutes = 1,
        RecoveryMargin = 0.5m,
    };
}
