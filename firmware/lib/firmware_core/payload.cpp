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
        flags |= SR_Q_IMPLAUSIBLE;
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
    // Rough but conservative: ~48 bytes per metric per sample plus ~200 bytes of envelope, against a 4 KB budget.
    const std::size_t estimated = 200 + (metricCount * 48 * sampleCount);
    return estimated <= SR_PAYLOAD_MAX_BYTES;
}

}  // namespace payload
}  // namespace sr
