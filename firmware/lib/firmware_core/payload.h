// Payload rules for the telemetry batch (§07-appendices/03 §3.2). Pure functions so the wire format and the
// plausibility rules are host-testable (TC-U-FW-03, TC-U-FW-07…09) instead of only observable on hardware.

#pragma once

#include <cstddef>
#include <cstdint>

namespace sr {
namespace payload {

/// Quality bitmask values carried on a sample.
///
/// These live here, in the library header, rather than in the firmware configuration header: they are part of
/// this API (the return value of `qualityFlagsFor`) and both the firmware and the host tests must see the same
/// names. They MUST stay bit-for-bit identical to the server's `QualityFlags` enum
/// (`backend/src/SmartReptile.Domain/Readings/QualityFlags.cs`) and to §07-appendices/03 §3.2.
enum QualityFlag : uint8_t {
    QualitySensorFault = 1u << 0,         ///< a sensor read failed; the server marks the metric unavailable
    QualityImplausible = 1u << 1,         ///< outside the metric's plausibility range (rule V-06)
    QualityFirstAfterBoot = 1u << 2,      ///< first reading after a reset; the sensor has not settled
    QualityBackfilled = 1u << 3,          ///< delivered from the ring buffer after an outage
    QualityClockUnsynced = 1u << 4,       ///< NTP had not synced when the sample was produced
    QualityCalibrationApplied = 1u << 5,  ///< a calibration offset was applied at ingest
};

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

/// Composes quality flags for one reading: adds the implausible bit when the value is out of range.
uint8_t qualityFlagsFor(Metric metric, float value, uint8_t baseFlags);

/// Seconds between the batch's base timestamp and this sample. Negative deltas (clock stepped backwards,
/// out-of-order buffered samples) clamp to 0 with SR_DELTA_INVALID, because the server rejects decreasing `t`.
constexpr uint32_t kInvalidDelta = 0xFFFFFFFFu;
uint32_t relativeSeconds(uint32_t baseEpoch, uint32_t sampleEpoch);

/// Encodes a float as centi-units (28.75 -> 2875) for the packed sample record.
int16_t encodeCenti(float value);

/// Decodes centi-units back to a float.
float decodeCenti(int16_t value);

/// Number of metrics in the wire format. Derived from the enum rather than hard-coded, so adding a metric
/// cannot quietly leave the payload-budget check — or the tests that call it — out of date.
constexpr std::size_t kMetricCount = static_cast<std::size_t>(Metric::SurfaceTemperature) + 1;

/// True when a batch of `sampleCount` samples × `metricCount` metrics stays inside the payload budget (rule
/// V-01). The estimate is calibrated against measured §3.2 payload sizes, not guessed — see `payload.cpp`.
bool isPayloadWithinBudget(std::size_t metricCount, std::size_t sampleCount);

}  // namespace payload
}  // namespace sr
