#include "filters.h"

#include <algorithm>

namespace sr {
namespace filter {

namespace {

/// Sorts a copy of the window and returns the middle element.
float medianOfSortedCopy(float* buffer, std::size_t count) {
    std::sort(buffer, buffer + count);
    return buffer[count / 2];
}

}  // namespace

FilterResult median(const float* samples, std::size_t count) {
    FilterResult result;

    if (samples == nullptr || count == 0) {
        return result;
    }

    const std::size_t usable = std::min<std::size_t>(count, 5);
    float buffer[5] = {0.0f, 0.0f, 0.0f, 0.0f, 0.0f};
    for (std::size_t i = 0; i < usable; ++i) {
        buffer[i] = samples[i];
    }

    result.value = medianOfSortedCopy(buffer, usable);
    result.spread = buffer[usable - 1] - buffer[0];
    result.valid = true;
    return result;
}

FilterResult medianWithOutlierCheck(const float* samples, std::size_t count, float noiseTolerance) {
    FilterResult result = median(samples, count);

    if (result.valid && noiseTolerance > 0.0f) {
        result.outlierDetected = result.spread > noiseTolerance;
    }

    return result;
}

float exponentialMovingAverage(float previous, float sample, float alpha) {
    if (alpha <= 0.0f) {
        return previous;
    }
    if (alpha >= 1.0f) {
        return sample;
    }

    return (alpha * sample) + ((1.0f - alpha) * previous);
}

float applyChain(const float* samples, std::size_t count, float noiseTolerance,
                 float previousEma, float alpha, bool& outlier) {
    const FilterResult pass = medianWithOutlierCheck(samples, count, noiseTolerance);
    outlier = pass.outlierDetected;

    if (!pass.valid) {
        return previousEma;
    }

    return exponentialMovingAverage(previousEma, pass.value, alpha);
}

}  // namespace filter
}  // namespace sr
