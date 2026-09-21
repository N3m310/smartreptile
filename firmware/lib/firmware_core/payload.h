// Payload rules for the telemetry batch (§07-appendices/03 §3.2). Pure functions so the wire format and the
// plausibility rules are host-testable (TC-U-FW-03, TC-U-FW-07…09) instead of only observable on hardware.

#pragma once

#include <cstddef>
#include <cstdint>

namespace sr {
namespace payload {

/// Metrics the node can report. The wire keys are short on purpose: a 12 h back-fill has to fit in 4 KB batches.
enum class Metric : uint8_t {
    AirTemperature = 0,
    RelativeHumidity = 1,
    Illuminance = 2,
    UvIndex = 3,
    SurfaceTemperature = 4,
};

/// Short JSON key used on the wire for a metric ("tf", "rh", "lux", "uvi", "st").
const char* metricKey(Metric metric);

/// Unit string used in logs and on the OLED.
const char* metricUnit(Metric metric);

/// True when the value lies inside the metric's plausibility bounds (rule V-06). Values outside are still
/// published, but flagged, so calibration problems remain visible instead of being thrown away.
bool isPlausible(Metric metric, float value);

/// Composes quality flags for one reading: adds SR_Q_IMPLAUSIBLE when the value is out of range.
uint8_t qualityFlagsFor(Metric metric, float value, uint8_t baseFlags);

/// Seconds between the batch's base timestamp and this sample. Negative deltas (clock stepped backwards,
/// out-of-order buffered samples) clamp to 0 with SR_DELTA_INVALID, because the server rejects decreasing `t`.
constexpr uint32_t kInvalidDelta = 0xFFFFFFFFu;
uint32_t relativeSeconds(uint32_t baseEpoch, uint32_t sampleEpoch);

/// Encodes a float as centi-units (28.75 -> 2875) for the packed sample record.
int16_t encodeCenti(float value);

/// Decodes centi-units back to a float.
float decodeCenti(int16_t value);

/// True when the key list for a sample stays inside the payload budget (rule V-01).
bool isPayloadWithinBudget(std::size_t metricCount, std::size_t sampleCount);

}  // namespace payload
}  // namespace sr
