#include "payload.h"

#include <cmath>

#include "sr_config.h"

namespace sr {
namespace payload {

namespace {

struct Bounds {
    float min;
    float max;
};

Bounds boundsFor(Metric metric) {
    switch (metric) {
        case Metric::AirTemperature:
            return {SR_TEMP_MIN_C, SR_TEMP_MAX_C};
        case Metric::RelativeHumidity:
            return {SR_RH_MIN_PCT, SR_RH_MAX_PCT};
        case Metric::Illuminance:
            return {SR_LUX_MIN, SR_LUX_MAX};
        case Metric::UvIndex:
            return {SR_UVI_MIN, SR_UVI_MAX};
        case Metric::SurfaceTemperature:
            return {SR_SURFACE_TEMP_MIN_C, SR_SURFACE_TEMP_MAX_C};
    }

    return {0.0f, 0.0f};
}

}  // namespace

const char* metricKey(Metric metric) {
    switch (metric) {
        case Metric::AirTemperature:
            return "tf";
        case Metric::RelativeHumidity:
            return "rh";
        case Metric::Illuminance:
            return "lux";
        case Metric::UvIndex:
            return "uvi";
        case Metric::SurfaceTemperature:
            return "st";
    }

    return "unknown";
}

const char* metricUnit(Metric metric) {
    switch (metric) {
        case Metric::AirTemperature:
        case Metric::SurfaceTemperature:
            return "C";
        case Metric::RelativeHumidity:
            return "%RH";
        case Metric::Illuminance:
            return "lx";
        case Metric::UvIndex:
            return "UVI";
    }

    return "";
}

bool isPlausible(Metric metric, float value) {
    const Bounds bounds = boundsFor(metric);

    // NaN would silently pass both comparisons, so reject it explicitly.
    if (std::isnan(value)) {
        return false;
    }

    return value >= bounds.min && value <= bounds.max;
}

uint8_t qualityFlagsFor(Metric metric, float value, uint8_t baseFlags) {
    uint8_t flags = baseFlags;

    if (!isPlausible(metric, value)) {
        flags |= QualityImplausible;
    }

    return flags;
}

uint32_t relativeSeconds(uint32_t baseEpoch, uint32_t sampleEpoch) {
    if (sampleEpoch < baseEpoch) {
        return kInvalidDelta;
    }

    return sampleEpoch - baseEpoch;
}

int16_t encodeCenti(float value) {
    const float scaled = value * 100.0f;
    const float clamped = scaled > 32767.0f ? 32767.0f : (scaled < -32767.0f ? -32767.0f : scaled);

    return static_cast<int16_t>(clamped >= 0.0f ? clamped + 0.5f : clamped - 0.5f);
}

float decodeCenti(int16_t value) {
    return static_cast<float>(value) / 100.0f;
}

bool isPayloadWithinBudget(std::size_t metricCount, std::size_t sampleCount) {
    // Sizing constants, worst case, for one metric in the §3.2 shape: a 3-char key plus quotes, colon, comma
    // and a sign + 4 integer digits + 2 decimals ("lux":-3276.8,) is 14 bytes, so 16 leaves headroom.
    //
    // These numbers replace a first attempt of `200 + metricCount * 48 * sampleCount`, which estimated 5000
    // bytes for the 5 × 20 batch that SR_BACKFILL_BATCH_MAX is documented to keep *under* the 4 KB budget —
    // i.e. the guard was refusing the very batch the configuration told the firmware to send. The previous
    // constants were calibrated against nothing; these are checked against the real serialisation, which
    // measures ~1.6 KB for 5 × 20 without the optional `raw` object and ~2.8 KB with it.
    //
    // `kRawMetricsPerSample` is counted even though M1 firmware does not emit `raw` yet: §3.2 permits it, so
    // the guard must not start under-reporting the day the sampling path grows one.
    //
    // Capacity check: 256 + 20 × (24 + 8 × 16) = 3296 bytes ≤ 4096, so a configured 20-sample batch fits with
    // ~20 % margin while a 120-sample batch (the server's cap) is still refused at 18496 bytes.
    constexpr std::size_t kEnvelopeBytes = 256;      // deviceId, seq, fw, ts, health, array brackets
    constexpr std::size_t kPerSampleBytes = 24;      // {"t":86400, … "q":63},
    constexpr std::size_t kPerMetricBytes = 16;      // "lux":-3276.8,
    constexpr std::size_t kRawMetricsPerSample = 3;  // the optional per-sample `raw` sub-object (§3.2)

    const std::size_t perSample = kPerSampleBytes + ((metricCount + kRawMetricsPerSample) * kPerMetricBytes);
    const std::size_t estimated = kEnvelopeBytes + (sampleCount * perSample);

    return estimated <= SR_PAYLOAD_MAX_BYTES;
}

}  // namespace payload
}  // namespace sr
