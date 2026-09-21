// Host tests for the filter chain — TC-U-FW-01…03.
// These run with `pio test -e native`: no hardware, no network, milliseconds.

#include <unity.h>

#include "filters.h"

void test_median_rejects_a_single_glitch(void);          // NOLINT(readability-identifier-naming) Unity naming
void test_median_of_short_window_returns_the_value(void);
void test_median_of_empty_window_is_invalid(void);
void test_outlier_detected_when_spread_exceeds_tolerance(void);
void test_outlier_not_detected_for_normal_drift(void);
void test_ema_converges_towards_a_step(void);
void test_ema_alpha_one_passes_the_sample_through(void);
void test_apply_chain_combines_median_and_ema(void);
void process(void);

void setUp(void) {}
void tearDown(void) {}

void process(void) {
    UNITY_BEGIN();

    RUN_TEST(test_median_rejects_a_single_glitch);
    RUN_TEST(test_median_of_short_window_returns_the_value);
    RUN_TEST(test_median_of_empty_window_is_invalid);
    RUN_TEST(test_outlier_detected_when_spread_exceeds_tolerance);
    RUN_TEST(test_outlier_not_detected_for_normal_drift);
    RUN_TEST(test_ema_converges_towards_a_step);
    RUN_TEST(test_ema_alpha_one_passes_the_sample_through);
    RUN_TEST(test_apply_chain_combines_median_and_ema);

    UNITY_END();
}

#ifdef ARDUINO
#include <Arduino.h>
void setup() {
    delay(2000);
    process();
}
void loop() {}
#else
int main(int argc, char** argv) {
    (void)argc;
    (void)argv;
    process();
    return 0;
}
#endif

// TC-U-FW-01 — a single I2C glitch must not reach the alert engine, otherwise a keeper gets woken up by a
// corrupted reading that never happened.
void test_median_rejects_a_single_glitch(void) {
    const float raw[5] = {41.0f, 41.1f, 55.0f, 41.2f, 41.1f};

    const sr::filter::FilterResult result = sr::filter::median(raw, 5);

    TEST_ASSERT_TRUE(result.valid);
    TEST_ASSERT_FLOAT_WITHIN(0.05f, 41.1f, result.value);
    TEST_ASSERT_TRUE(result.spread > 10.0f);   // the glitch is visible in the spread for the outlier check
}

void test_median_of_short_window_returns_the_value(void) {
    const float raw[1] = {28.6f};

    const sr::filter::FilterResult result = sr::filter::median(raw, 1);

    TEST_ASSERT_TRUE(result.valid);
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 28.6f, result.value);
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 0.0f, result.spread);
}

void test_median_of_empty_window_is_invalid(void) {
    const sr::filter::FilterResult result = sr::filter::median(nullptr, 0);

    TEST_ASSERT_FALSE(result.valid);
}

// TC-U-FW-01/02 — the outlier flag drives quality bit 2, which excludes the reading from evaluation.
void test_outlier_detected_when_spread_exceeds_tolerance(void) {
    const float raw[5] = {41.0f, 41.1f, 55.0f, 41.2f, 41.1f};

    const sr::filter::FilterResult result = sr::filter::medianWithOutlierCheck(raw, 5, 0.5f);

    TEST_ASSERT_TRUE(result.outlierDetected);
}

void test_outlier_not_detected_for_normal_drift(void) {
    const float raw[5] = {28.70f, 28.74f, 28.72f, 28.76f, 28.73f};

    const sr::filter::FilterResult result = sr::filter::medianWithOutlierCheck(raw, 5, 0.5f);

    TEST_ASSERT_FALSE(result.outlierDetected);
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 28.73f, result.value);
}

// TC-U-FW-02 — a step must be approached monotonically: an EMA that oscillates would create fake crossings.
void test_ema_converges_towards_a_step(void) {
    float value = 20.0f;

    for (int i = 0; i < 60; ++i) {
        const float next = sr::filter::exponentialMovingAverage(value, 30.0f, 0.3f);
        TEST_ASSERT_TRUE(next >= value);        // monotonic increase
        TEST_ASSERT_TRUE(next <= 30.0f);        // no overshoot
        value = next;
    }

    TEST_ASSERT_FLOAT_WITHIN(0.01f, 30.0f, value);
}

void test_ema_alpha_one_passes_the_sample_through(void) {
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 42.0f, sr::filter::exponentialMovingAverage(10.0f, 42.0f, 1.0f));
}

void test_apply_chain_combines_median_and_ema(void) {
    const float raw[5] = {27.0f, 27.1f, 27.2f, 27.1f, 27.1f};
    bool outlier = true;

    // Previous EMA 26.0 with alpha 0.5 and a median of 27.1 → 26.55
    const float value = sr::filter::applyChain(raw, 5, 0.5f, 26.0f, 0.5f, outlier);

    TEST_ASSERT_FALSE(outlier);
    TEST_ASSERT_FLOAT_WITHIN(0.01f, 26.55f, value);
}
