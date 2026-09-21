// Host tests for the sample ring buffer — TC-U-FW-04…06.
// This is the reliability core: if it loses or reorders samples quietly, the dataset is worthless and the
// coverage numbers lie.

#include <unity.h>

#include "ring_buffer.h"

namespace {

sr::storage::SampleRecord makeRecord(uint64_t sequence) {
    sr::storage::SampleRecord record{};
    record.epoch = static_cast<uint32_t>(1700000000ULL + sequence);
    record.sequence = sequence;
    record.tempCx100 = static_cast<int16_t>(2800 + sequence);
    record.rhx100 = 4100;
    record.lux = 1200;
    record.surfaceCx100 = sr::storage::kNoSurfaceReading;
    record.qualityFlags = 0;
    return record;
}

}  // namespace

void test_ring_buffer_keeps_insertion_order(void);
void test_ring_buffer_reports_eviction_and_overwrites_oldest(void);
void test_ring_buffer_drop_oldest_removes_from_the_front(void);
void test_ring_buffer_survives_a_serialize_round_trip(void);
void test_ring_buffer_rejects_a_misaligned_blob(void);
void test_sample_record_round_trips_surface_temperature(void);
void process_ring(void);

void setUp(void) {}
void tearDown(void) {}

void process_ring(void) {
    UNITY_BEGIN();

    RUN_TEST(test_ring_buffer_keeps_insertion_order);
    RUN_TEST(test_ring_buffer_reports_eviction_and_overwrites_oldest);
    RUN_TEST(test_ring_buffer_drop_oldest_removes_from_the_front);
    RUN_TEST(test_ring_buffer_survives_a_serialize_round_trip);
    RUN_TEST(test_ring_buffer_rejects_a_misaligned_blob);
    RUN_TEST(test_sample_record_round_trips_surface_temperature);

    UNITY_END();
}

#ifdef ARDUINO
#include <Arduino.h>
void setup() {
    delay(2000);
    process_ring();
}
void loop() {}
#else
int main(int argc, char** argv) {
    (void)argc;
    (void)argv;
    process_ring();
    return 0;
}
#endif

// TC-U-FW-04
void test_ring_buffer_keeps_insertion_order(void) {
    sr::storage::RingBuffer buffer;

    for (uint64_t i = 1; i <= 10; ++i) {
        buffer.push(makeRecord(i));
    }

    TEST_ASSERT_EQUAL_UINT32(10, buffer.size());
    TEST_ASSERT_EQUAL_UINT32(0, buffer.droppedCount());

    for (std::size_t i = 0; i < 10; ++i) {
        sr::storage::SampleRecord record;
        TEST_ASSERT_TRUE(buffer.at(i, record));
        TEST_ASSERT_EQUAL_UINT64(i + 1, record.sequence);
    }
}

// TC-U-FW-05 — losing data is allowed when 12 h of buffer is exceeded; losing it silently is not.
void test_ring_buffer_reports_eviction_and_overwrites_oldest(void) {
    sr::storage::RingBuffer buffer;

    for (uint64_t i = 1; i <= sr::storage::RingBuffer::kCapacity + 5; ++i) {
        buffer.push(makeRecord(i));
    }

    TEST_ASSERT_EQUAL_UINT32(sr::storage::RingBuffer::kCapacity, buffer.size());
    TEST_ASSERT_EQUAL_UINT32(5, buffer.droppedCount());
    TEST_ASSERT_TRUE(buffer.isFull());

    sr::storage::SampleRecord oldest;
    TEST_ASSERT_TRUE(buffer.at(0, oldest));
    TEST_ASSERT_EQUAL_UINT64(6, oldest.sequence);   // samples 1…5 were evicted
}

void test_ring_buffer_drop_oldest_removes_from_the_front(void) {
    sr::storage::RingBuffer buffer;

    for (uint64_t i = 1; i <= 20; ++i) {
        buffer.push(makeRecord(i));
    }

    TEST_ASSERT_EQUAL_UINT32(10, buffer.dropOldest(10));
    TEST_ASSERT_EQUAL_UINT32(10, buffer.size());

    sr::storage::SampleRecord first;
    TEST_ASSERT_TRUE(buffer.at(0, first));
    TEST_ASSERT_EQUAL_UINT64(11, first.sequence);

    // Dropping more than is buffered is clamped, not undefined.
    TEST_ASSERT_EQUAL_UINT32(10, buffer.dropOldest(999));
    TEST_ASSERT_EQUAL_UINT32(0, buffer.size());
}

// TC-U-FW-06 — the buffer is persisted to NVS as one blob, so a reset must not lose or corrupt it.
void test_ring_buffer_survives_a_serialize_round_trip(void) {
    sr::storage::RingBuffer source;

    for (uint64_t i = 1; i <= 25; ++i) {
        sr::storage::SampleRecord record = makeRecord(i);
        record.tempCx100 = static_cast<int16_t>(-1500 + static_cast<int16_t>(i));
        source.push(record);
    }

    static uint8_t blob[sr::storage::RingBuffer::kCapacity * sizeof(sr::storage::SampleRecord)];
    const std::size_t written = source.serialize(blob, sizeof(blob));
    TEST_ASSERT_EQUAL_UINT32(25 * sizeof(sr::storage::SampleRecord), written);

    sr::storage::RingBuffer restored;
    TEST_ASSERT_EQUAL_UINT32(25, restored.deserialize(blob, written));
    TEST_ASSERT_EQUAL_UINT32(25, restored.size());

    for (std::size_t i = 0; i < 25; ++i) {
        sr::storage::SampleRecord a;
        sr::storage::SampleRecord b;
        source.at(i, a);
        restored.at(i, b);
        TEST_ASSERT_EQUAL_UINT64(a.sequence, b.sequence);
        TEST_ASSERT_EQUAL_INT16(a.tempCx100, b.tempCx100);
        TEST_ASSERT_EQUAL_UINT32(a.epoch, b.epoch);
    }
}

void test_ring_buffer_rejects_a_misaligned_blob(void) {
    static uint8_t blob[64] = {0};
    sr::storage::RingBuffer buffer;

    TEST_ASSERT_EQUAL_UINT32(0, buffer.deserialize(blob, 0));
    TEST_ASSERT_EQUAL_UINT32(0, buffer.deserialize(blob, 7));          // not a multiple of the record size
    TEST_ASSERT_EQUAL_UINT32(0, buffer.deserialize(nullptr, 32));
}

// TC-U-FW-08 — the surface probe is optional: "not fitted" must be distinguishable from 0.00 °C.
void test_sample_record_round_trips_surface_temperature(void) {
    sr::storage::SampleRecord withoutProbe = makeRecord(1);
    TEST_ASSERT_FALSE(sr::storage::hasSurfaceReading(withoutProbe));

    sr::storage::SampleRecord withProbe = makeRecord(2);
    withProbe.surfaceCx100 = 3120;
    TEST_ASSERT_TRUE(sr::storage::hasSurfaceReading(withProbe));
    TEST_ASSERT_FLOAT_WITHIN(0.001f, 31.20f, static_cast<float>(withProbe.surfaceCx100) / 100.0f);
}
