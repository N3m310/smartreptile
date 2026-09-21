// Pure filtering helpers (ADR-014). No Arduino headers on purpose: this file is compiled for the host in
// the `native` environment and unit-tested as TC-U-FW-01…03.

#pragma once

#include <cstddef>
#include <cstdint>

namespace sr {
namespace filter {

/// Result of one filtering pass.
struct FilterResult {
    float value = 0.0f;              // filtered value used for publishing and (indirectly) evaluation
    float spread = 0.0f;             // max - min of the raw window, used for the outlier check
    bool outlierDetected = false;    // true when the spread exceeds the plausible sensor noise
    bool valid = false;              // false when the window contained no usable reading
};

/// Median of up to 5 raw readings. Rejects a single I2C glitch — the most common cause of phantom alerts.
/// @param samples raw readings
/// @param count number of readings (1..5)
FilterResult median(const float* samples, std::size_t count);

/// Median-of-5 followed by the outlier test.
/// @param samples raw readings
/// @param count number of readings
/// @param noiseTolerance plausible noise band; a spread larger than this sets `outlierDetected`
FilterResult medianWithOutlierCheck(const float* samples, std::size_t count, float noiseTolerance);

/// Exponential moving average, the second stage of the filter chain.
/// @param previous previous EMA value; pass the first sample for the initial call
/// @param sample new filtered sample
/// @param alpha smoothing factor (0 < alpha <= 1)
float exponentialMovingAverage(float previous, float sample, float alpha);

/// Convenience wrapper: does one complete pass (median + outlier check + EMA) and returns the value to publish.
/// @param samples raw readings
/// @param count number of readings
/// @param noiseTolerance plausible noise band
/// @param previousEma previous EMA value
/// @param alpha smoothing factor
/// @param[out] outlier receives the outlier flag
float applyChain(const float* samples, std::size_t count, float noiseTolerance,
                 float previousEma, float alpha, bool& outlier);

}  // namespace filter
}  // namespace sr
