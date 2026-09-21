// SmartReptile sensor node — M1 skeleton (§03-implementation/04, roadmap M1 task 1.5).
//
// What this firmware does today:
//   * boots, logs its identity and the compile-time configuration,
//   * blinks the status LED so the board can be identified on the bench,
//   * runs a 1 Hz tick that will host the sampler task.
//
// What M2 adds (the TODOs below are the plan, not decoration):
//   * sensor drivers (SHT31 / BH1750 / LTR390 / DS18B20) behind an ISensor interface,
//   * the filter chain and the ring buffer write,
//   * Wi-Fi provisioning + NTP, then the MQTT/TLS transport with LWT and back-fill.
//
// Design rule this file must keep: no thresholds, no severity, no alert decisions. The device reports values,
// quality flags, faults and health only (ADR-005) — that is what lets bands be edited without reflashing.

#include <Arduino.h>

#include "config.h"
#include "filters.h"
#include "payload.h"
#include "ring_buffer.h"

namespace {

sr::storage::RingBuffer g_ringBuffer;
sr::filter::FilterResult g_lastTemperatureFilter;

uint32_t g_lastSampleMs = 0;
uint32_t g_lastLedToggleMs = 0;
uint32_t g_sequence = 0;
bool g_ledOn = false;

/// Placeholder for the sampling pipeline. M2 replaces the synthetic values with real sensor reads followed by
/// `sr::filter::applyChain`, then pushes the record into the ring buffer and lets the transport task publish it.
sr::storage::SampleRecord takeSample(uint32_t nowMs) {
    sr::storage::SampleRecord record{};

    record.epoch = 0;                     // M2: NTP time; SR_Q_CLOCK_UNSYNCED is set until the first sync
    record.sequence = ++g_sequence;
    record.qualityFlags = SR_Q_FIRST_AFTER_BOOT;

#if SR_BENCH_MODE
    // Bench mode: a smooth synthetic curve so the server pipeline can be exercised without hardware
    // (§07-appendices/04 §6). This never ships (SR_BENCH_MODE = 0 in the release build).
    const float phase = static_cast<float>(nowMs % 86400000UL) / 86400000.0f;
    const float temperature = 28.0f + (1.5f * sinf(phase * 2.0f * PI));
    const float humidity = 35.0f + (5.0f * sinf(phase * 4.0f * PI));
    const float lux = (phase > 0.3f && phase < 0.8f) ? 1500.0f : 0.0f;

    record.tempCx100 = sr::payload::encodeCenti(temperature);
    record.rhx100 = static_cast<uint16_t>(sr::payload::encodeCenti(humidity));
    record.lux = static_cast<uint32_t>(lux);
    record.surfaceCx100 = sr::storage::kNoSurfaceReading;
#else
    // Real hardware path: fill in M2.
    record.tempCx100 = sr::payload::encodeCenti(0.0f);
    record.rhx100 = static_cast<uint16_t>(sr::payload::encodeCenti(0.0f));
    record.lux = 0;
    record.surfaceCx100 = sr::storage::kNoSurfaceReading;
#endif

    return record;
}

void logConfiguration() {
    Serial.println();
    Serial.println(F("-------------------------------------------------------------"));
    Serial.print(F("SmartReptile node  fw "));
    Serial.println(F(SR_FW_VERSION));
    Serial.print(F("bench mode: "));
    Serial.println(SR_BENCH_MODE ? F("ON (synthetic sensors)") : F("off (real sensors)"));
    Serial.print(F("sampling interval: "));
    Serial.print(SR_SAMPLING_INTERVAL_SEC);
    Serial.println(F(" s"));
    Serial.print(F("ring buffer: "));
    Serial.print(sr::storage::RingBuffer::kCapacity);
    Serial.print(F(" samples = "));
    Serial.print(sr::storage::RingBuffer::serializedSize(sr::storage::RingBuffer::kCapacity) / 1024);
    Serial.println(F(" KB"));
    Serial.print(F("I2C: SDA="));
    Serial.print(SR_I2C_SDA);
    Serial.print(F(" SCL="));
    Serial.print(SR_I2C_SCL);
    Serial.print(F("  1-Wire: GPIO"));
    Serial.println(SR_ONE_WIRE_PIN);
    Serial.println(F("M2 next: sensor drivers, Wi-Fi provisioning + claim code, MQTT/TLS transport"));
    Serial.println(F("-------------------------------------------------------------"));
}

void updateStatusLed(uint32_t nowMs) {
    // A single LED has to say four things, so it blinks in patterns rather than pretending to be a screen.
    if (nowMs - g_lastLedToggleMs < 500) {
        return;
    }

    g_lastLedToggleMs = nowMs;
    g_ledOn = !g_ledOn;
    digitalWrite(SR_STATUS_LED_PIN, g_ledOn ? HIGH : LOW);
}

}  // namespace

void setup() {
    Serial.begin(115200);
    delay(200);  // let the USB-serial bridge attach before the first line is printed

    pinMode(SR_STATUS_LED_PIN, OUTPUT);
    pinMode(SR_FACTORY_RESET_PIN, INPUT_PULLUP);

    g_ringBuffer.clear();
    g_lastSampleMs = millis();

    logConfiguration();
}

void loop() {
    const uint32_t nowMs = millis();

    updateStatusLed(nowMs);

    // 1 Hz tick; the sample is emitted every SR_SAMPLING_INTERVAL_SEC (M2 moves this into a FreeRTOS task
    // pinned to core 1, with the transport task on core 0 — §03-implementation/04 §1).
    if (nowMs - g_lastSampleMs >= (SR_SAMPLING_INTERVAL_SEC * 1000UL)) {
        g_lastSampleMs = nowMs;

        const sr::storage::SampleRecord record = takeSample(nowMs);
        g_ringBuffer.push(record);

        Serial.print(F("sample seq="));
        Serial.print(record.sequence);
        Serial.print(F(" buf="));
        Serial.print(g_ringBuffer.size());
        Serial.print(F("/"));
        Serial.print(sr::storage::RingBuffer::kCapacity);
        Serial.print(F(" dropped="));
        Serial.print(g_ringBuffer.droppedCount());
        Serial.print(F(" flags=0x"));
        Serial.println(record.qualityFlags, HEX);
    }
}
