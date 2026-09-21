// Firmware configuration shared by both environments (§03-implementation/04 §2, §07-appendices/04 §2).
//
// Rules of thumb for this file:
//   * No business rules live here. Thresholds, severity and alert decisions belong to the server (ADR-005) —
//     the firmware only reports values, quality flags, faults and health.
//   * Everything a test needs to assert on is a plain constant or a pure function, not a hardware call.

#pragma once

// ---------------------------------------------------------------------------------------------
// Identity and version
// ---------------------------------------------------------------------------------------------
#ifndef SR_FW_VERSION
#define SR_FW_VERSION "0.1.0-dev"
#endif

// ---------------------------------------------------------------------------------------------
// Timing
// ---------------------------------------------------------------------------------------------
#define SR_SAMPLING_INTERVAL_SEC 60   // FR-06 default; overridable by the `set_config` command
#define SR_MIN_SAMPLING_INTERVAL_SEC 10
#define SR_MAX_SAMPLING_INTERVAL_SEC 300
#define SR_HEALTH_INTERVAL_SEC 300
#define SR_SAMPLE_TICK_MS 1000

// Number of raw reads per sample that are combined by the median filter (ADR-014).
#define SR_FILTER_SAMPLES 5
#define SR_FILTER_SPACING_MS 50

// EMA smoothing factor applied after the median (0 < alpha <= 1; 0.3 damps noise without hiding trends).
#define SR_EMA_ALPHA 0.3f

// A sample whose readings disagree by more than this multiple of the datasheet noise is flagged as implausible.
#define SR_OUTLIER_NOISE_MULTIPLIER 2.0f

// ---------------------------------------------------------------------------------------------
// Pin map (ESP32 DevKitC v4) — must match §07-appendices/04 §2
// ---------------------------------------------------------------------------------------------
#define SR_I2C_SDA 21
#define SR_I2C_SCL 22
#define SR_ONE_WIRE_PIN 4
#define SR_STATUS_LED_PIN 2
#define SR_FACTORY_RESET_PIN 0       // BOOT button: hold 5 s to clear the secret and re-enter provisioning

// I2C addresses
#define SR_SHT31_ADDRESS 0x44
#define SR_BH1750_ADDRESS 0x23
#define SR_LTR390_ADDRESS 0x53
#define SR_OLED_ADDRESS 0x3C

// ---------------------------------------------------------------------------------------------
// Plausibility bounds (rule V-06). Duplicated from the server's metric dictionary on purpose: obviously
// bad values should be flagged before they consume bandwidth. Both sides must agree.
// ---------------------------------------------------------------------------------------------
#define SR_TEMP_MIN_C (-10.0f)
#define SR_TEMP_MAX_C (60.0f)
#define SR_RH_MIN_PCT (0.0f)
#define SR_RH_MAX_PCT (100.0f)
#define SR_LUX_MIN (0.0f)
#define SR_LUX_MAX (200000.0f)
#define SR_UVI_MIN (0.0f)
#define SR_UVI_MAX (15.0f)
#define SR_SURFACE_TEMP_MIN_C (-10.0f)
#define SR_SURFACE_TEMP_MAX_C (80.0f)

// ---------------------------------------------------------------------------------------------
// Ring buffer: 12 h at 60 s = 720 samples = 23 KB, which is the NFR-03 floor (BR-07.3).
// ---------------------------------------------------------------------------------------------
#define SR_RING_CAPACITY 720
#define SR_BACKFILL_BATCH_MAX 20     // samples per publish while draining the buffer (keeps payloads < 4 KB)
#define SR_PAYLOAD_MAX_BYTES 4096

// ---------------------------------------------------------------------------------------------
// MQTT topics (§07-appendices/03 §3.1)
// ---------------------------------------------------------------------------------------------
#define SR_TOPIC_PREFIX "sr/v1/d"
#define SR_TOPIC_TELEMETRY "telemetry"
#define SR_TOPIC_HEALTH "health"
#define SR_TOPIC_STATUS "status"
#define SR_TOPIC_EVENTS "events"
#define SR_TOPIC_CMD "cmd"
#define SR_TOPIC_ACK "ack"

// ---------------------------------------------------------------------------------------------
// MQTT cadence and backoff (§03-implementation/04 §4)
// ---------------------------------------------------------------------------------------------
#define SR_MQTT_KEEPALIVE_SEC 30
#define SR_MQTT_RECONNECT_MIN_MS 2000
#define SR_MQTT_RECONNECT_MAX_MS 60000
#define SR_MQTT_FALLBACK_AFTER_MS 60000   // switch to HTTPS POST after 60 s without an MQTT connection
#define SR_NTP_RESYNC_INTERVAL_SEC 86400

// ---------------------------------------------------------------------------------------------
// Quality flags — the low byte is shared with the server's QualityFlags enum (readings/QualityFlags.cs)
// ---------------------------------------------------------------------------------------------
#define SR_Q_SENSOR_FAULT 0x01
#define SR_Q_IMPLAUSIBLE 0x02
#define SR_Q_FIRST_AFTER_BOOT 0x04
#define SR_Q_BACKFILLED 0x08
#define SR_Q_CLOCK_UNSYNCED 0x10
#define SR_Q_CALIBRATION_APPLIED 0x20

// ---------------------------------------------------------------------------------------------
// Bench mode (never released): swaps real sensors for a synthetic profile so the alert path can be
// demonstrated repeatably without heating anything — §07-appendices/04 §6.
// ---------------------------------------------------------------------------------------------
#ifndef SR_BENCH_MODE
#define SR_BENCH_MODE 0
#endif
