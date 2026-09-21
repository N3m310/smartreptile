using FluentAssertions;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>Metric dictionary and quality-flag rules — TC-U-06/TC-U-07/TC-U-08 in §04-quality/02.</summary>
public class MetricAndQualityRulesTests
{
    [Theory]
    [Trait("TestCase", "TC-U-06")]
    [InlineData(-10, true)]
    [InlineData(60, true)]
    [InlineData(60.01, false)]
    [InlineData(85, false)]
    public void GivenTemperature_ThenPlausibilityBoundariesMatchTheDictionary(double value, bool expectedPlausible)
    {
        MetricDictionary.IsPlausible(MetricCode.TempC, (decimal)value).Should().Be(expectedPlausible);
    }

    [Theory]
    [Trait("TestCase", "TC-U-07")]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(100.1, false)]
    [InlineData(-0.1, false)]
    public void GivenHumidity_ThenPlausibilityBoundariesMatchTheDictionary(double value, bool expectedPlausible)
    {
        MetricDictionary.IsPlausible(MetricCode.HumidityPct, (decimal)value).Should().Be(expectedPlausible);
    }

    [Fact]
    [Trait("TestCase", "TC-U-08")]
    public void GivenNegativeIlluminance_ThenItIsImplausible()
    {
        MetricDictionary.IsPlausible(MetricCode.LightLux, -5m).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-06")]
    public void GivenImplausibleValue_ThenTheImplausibleFlagIsSetAndTheSampleIsNotEvaluable()
    {
        var flags = QualityRules.ForReading(MetricCode.TempC, 85m, QualityFlags.None);

        flags.Should().HaveFlag(QualityFlags.Implausible);
        QualityRules.IsEvaluable(flags).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-06")]
    public void GivenPlausibleValue_ThenNothingIsFlagged()
    {
        var flags = QualityRules.ForReading(MetricCode.TempC, 28.75m, QualityFlags.None);

        flags.Should().Be(QualityFlags.None);
        QualityRules.IsEvaluable(flags).Should().BeTrue();
    }

    [Fact]
    [Trait("TestCase", "TC-U-19")]
    public void GivenBackfilledAndCalibratedSample_ThenItIsStillEvaluable()
    {
        var flags = QualityFlags.Backfilled | QualityFlags.CalibrationApplied;

        QualityRules.IsEvaluable(flags).Should().BeTrue(
            "a back-filled batch is real data: it is reported, it just does not notify for old excursions");
    }

    [Fact]
    [Trait("TestCase", "TC-U-28")]
    public void GivenSensorFault_ThenTheSampleIsNotEvaluable()
    {
        QualityRules.IsEvaluable(QualityFlags.SensorFault).Should().BeFalse();
    }

    [Fact]
    public void GivenEveryMetric_ThenApiKeysAndUnitsAreDistinctAndPresent()
    {
        MetricDictionary.All.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.ApiKey));
        MetricDictionary.All.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Unit));
        MetricDictionary.All.Select(m => m.ApiKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GivenCoreMetrics_ThenTheFourRequiredMetricsAreMarkedCore()
    {
        var core = MetricDictionary.All.Where(m => m.IsCore).Select(m => m.Code).ToList();

        core.Should().Contain(new[] { MetricCode.TempC, MetricCode.HumidityPct, MetricCode.LightLux, MetricCode.UvIndex });
    }
}
