// Host tests for the wire-format rules — TC-U-FW-03, TC-U-FW-07…09.
// These guard the contract in §07-appendices/03 §3.2: if the firmware and the server validator disagree about
// keys, ranges or the `t` offset, telemetry is rejected at the door and nobody notices until the chart is empty.

#include <unity.h>

#include "payload.h"

void process_payload(void);

void setUp(void) {}
void tearDown(void) {}

void process_payload(void) {
    UNITY_BEGIN();

    RUN_TEST(test_temperature_plausibility_boundaries);
    RUN_TEST(test_humidity_and_lux_plausibility_boundaries);
    RUN_TEST(test_implausible_value_is_flagged_not_dropped);
    RUN_TEST(test_quality_flags_preserve_base_bits);
    RUN_TEST(test_relative_seconds_are_ascending_within_a_batch);
    RUN_TEST(test_relative_seconds_reject_out_of_order_samples);
    RUN_TEST(test_centi_encoding_round_trips);
    RUN_TEST(test_payload_budget_allows_a_backfill_batch);

    UNITY_END();
}

#ifdef ARDUINO
#include <Arduino.h>
void setup() {
    delay(2000);
    process_payload();
}
void loop() {}
#else
int main(int argc, char** argv) {
    (void)argc;
    (void)argv;
    process_payload();
    return 0;
}
#endif

// TC-U-FW-03 / TC-U-FW-09 — the bounds are duplicated from the server's metric dictionary, so the boundary
// values are asserted explicitly.
void test_temperature_plausibility_boundaries(void) {
    TEST_ASSERT_TRUE(sr::payload::isPlausible(sr::payload::Metric::AirTemperature, -10.0f));
    TEST_ASSERT_TRUE(sr::payload::isPlausible(sr::payload::Metric::AirTemperature, 60.0f));
    TEST_ASSERT_FALSE(sr::payload::isPlausible(sr::payload::Metric::AirTemperature, 60.01f));
    TEST_ASSERT_FALSE(sr::payload::isPlausible(sr::payload::Metric::AirTemperature, 85.0f));
}

void test_humidity_and_lux_plausibility_boundaries(void) {
    TEST_ASSERT_TRUE(sr::payload::isPlausible(sr::payload::Metric::RelativeHumidity, 0.0f));
    TEST_ASSERT_TRUE(sr::payload::isPlausible(sr::payload::Metric::RelativeHumidity, 100.0f));
    TEST_ASSERT_FALSE(sr::payload::isPlausible(sr::payload::Metric::RelativeHumidity, 100.1f));

    TEST_ASSERT_FALSE(sr::payload::isPlausible(sr::payload::Metric::Illuminance, -5.0f));
    TEST_ASSERT_TRUE(sr::payload::isPlausible(sr::payload::Metric::Illuminance, 200000.0f));
    TEST_ASSERT_FALSE(sr::payload::isPlausible(sr::payload::Metric::Illuminance, 200001.0f));
}

void test_implausible_value_is_flagged_not_dropped(void) {
    const uint8_t flags = sr::payload::qualityFlagsFor(sr::payload::Metric::AirTemperature, 85.0f, 0);

    TEST_ASSERT_BITS_HIGH(SR_Q_IMPLAUSIBLE, flags);
}

void test_quality_flags_preserve_base_bits(void) {
    const uint8_t flags = sr::payload::qualityFlagsFor(
        sr::payload::Metric::AirTemperature, 28.5f, SR_Q_BACKFILLED | SR_Q_CALIBRATION_APPLIED);

    TEST_ASSERT_EQUAL_UINT8(SR_Q_BACKFILLED | SR_Q_CALIBRATION_APPLIED, flags);
}

// TC-U-FW-07 — `t` is a seconds offset from the batch timestamp and must increase; the server rejects
// decreasing offsets because they would break per-device ordering (BR-06.6).
void test_relative_seconds_are_ascending_within_a_batch(void) {
    const uint32_t base = 1800000000UL;

    TEST_ASSERT_EQUAL_UINT32(0, sr::payload::relativeSeconds(base, base));
    TEST_ASSERT_EQUAL_UINT32(60, sr::payload::relativeSeconds(base, base + 60));
    TEST_ASSERT_EQUAL_UINT32(120, sr::payload::relativeSeconds(base, base + 120));
}

void test_relative_seconds_reject_out_of_order_samples(void) {
    const uint32_t base = 1800000000UL;

    TEST_ASSERT_EQUAL_UINT32(sr::payload::kInvalidDelta, sr::payload::relativeSeconds(base, base - 1));
}

void test_centi_encoding_round_trips(void) {
    TEST_ASSERT_EQUAL_INT16(2875, sr::payload::encodeCenti(28.75f));
    TEST_ASSERT_EQUAL_INT16(4120, sr::payload::encodeCenti(41.20f));
    TEST_ASSERT_EQUAL_INT16(-40, sr::payload::encodeCenti(-0.40f));
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 28.75f, sr::payload::decodeCenti(2875));
    TEST_ASSERT_FLOAT_WITHIN(0.001f, -0.40f, sr::payload::decodeCenti(-40));
}

void test_payload_budget_allows_a_backfill_batch(void) {
    // 5 metrics × 20 samples (the back-fill batch size) must fit the 4 KB payload budget.
    TEST_ASSERT_TRUE(sr::payload::isPayloadWithinBudget(5, 20));

    // …and the budget must actually bite rather than being decorative.
    TEST_ASSERT_FALSE(sr::payload::isPayloadWithinBudget(5, 120));
}
