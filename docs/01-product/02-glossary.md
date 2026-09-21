# 02 — Glossary

Terms are listed in the sense used by this doc set. Where a term has a public standard
meaning, the source is named.

## Domain — animal and habitat

| Term | Meaning in SmartReptile |
|---|---|
| **Terrarium** | The monitored enclosure (the brief specifies a 20×10 cm glass box, a miniature ecosystem). Modelled as `Terrarium`; exactly one device may be bound to it in v1. |
| **Microclimate** | The temperature/humidity gradient inside a terrarium: warm side vs cool side, surface vs air. SmartReptile measures **air** temperature/humidity at the sensor position and, if a probe is fitted, **surface** temperature (substrate or basking spot). |
| **Basking spot** | The heated surface a reptile uses to raise body temperature. Tracked via the optional `SurfaceTempC` metric. |
| **Ectotherm** | An animal whose body temperature is driven by the environment. Explains why *duration* out of range matters as much as *magnitude*. |
| **Species profile** | A named set of environmental bands for one species or climate zone (e.g. "Leopard gecko — semi-desert"). Table `SpeciesProfile`. |
| **Climate zone** | Coarse grouping used for the seeded profiles: `Tropical`, `SemiArid`, `Arid`, `Temperate`. Determines which bands are seeded. |
| **Photoperiod** | The daily light/dark schedule, defined per species profile as `photoperiodHours` + `lightsOnLocalTime`. Drives day/night threshold phases and the light-hours report. |
| **Day phase / Night phase** | The two (optionally three: `Any`) evaluation windows. A metric may have different bands in day vs night (e.g. a night drop of 3–5 °C is *desirable*, not an alert). |
| **Gravid / shedding** | Physiological states that temporarily shift requirements (e.g. humidity must rise during shedding). Not modelled in v1; recorded as free-text notes on the terrarium. |
| **UV index (UVI)** | Unitless measure of erythemally weighted UV irradiance on a horizontal plane. Reptiles need UVI ranges for vitamin D₃ synthesis; the sensor reports the index, **not** a UVB dose in µW/cm². |
| **Light hours** | Accumulated hours per day where `LightLux` exceeds the profile's `lightThresholdLux`. |

## Domain — measurement

| Term | Meaning in SmartReptile |
|---|---|
| **Reading** | A single metric value with a timestamp. Stored as `MetricReading(MetricId, Value, Unit)`. |
| **Sample** | One device report containing all metrics measured at the same instant, plus device health. Stored as `TelemetrySample`; groups its readings. |
| **Metric** | A named, typed quantity in the dictionary table `Metric`: `TempC`, `HumidityPct`, `LightLux`, `UvIndex`, `SurfaceTempC`, `BatteryPct`, `RssiDbm`. |
| **Raw value** | The unprocessed sensor output kept for diagnostics (`RawValue` column / `raw` field in the MQTT payload). |
| **Filtered value** | The value after on-device smoothing (median-of-5 + EMA) that is used for thresholding. |
| **Quality flag** | Bitmask on a sample: `0` = OK, `1` = sensor fault, `2` = out of physical plausibility, `4` = stale/first reading after boot, `8` = back-filled offline batch, `16` = calibration applied. |
| **Stale reading** | No sample received for more than `3 × samplingInterval`. Produces a `DeviceSilent` alert rather than a metric alert. |
| **Calibration offset** | A per-device, per-metric additive correction stored on `Device` (`calibrationJson`), applied at ingest so historical raw data stays untouched. |
| **Sampling interval** | Device-side period between readings. Default 60 s, configurable 10–300 s by downlink command. |
| **Publish interval** | Device-side period between MQTT batches. Default 60 s (one sample per batch) or 300 s when buffering offline. |

## Domain — thresholds and alerting

| Term | Meaning in SmartReptile |
|---|---|
| **Threshold band** | The acceptable interval `[TargetMin, TargetMax]` for a metric in a phase. Leaving it is a *Warning*. |
| **Critical band** | The wider `[CriticalMin, CriticalMax]`. Leaving it is a *Critical* alert. Must satisfy `CriticalMin ≤ TargetMin ≤ TargetMax ≤ CriticalMax`. |
| **Dwell time** | How long a value must stay outside a band before an alert is raised (default 5 min for Warning, 2 min for Critical). Prevents alert spam from a single noisy sample. |
| **Hysteresis / recovery margin** | Extra distance *inside* the band required to consider the condition recovered (default 0.5 °C, 3 %RH, 5% of the lux threshold). Prevents flapping on the boundary. |
| **Cooldown** | Minimum time between two notifications for the same open alert (default 60 min). |
| **Dedupe key** | `{terrariumId}:{metric}:{severity}:{phase}` — guarantees at most one open alert per key. |
| **Alert** | A persisted event with severity, the triggering value, and an open → acknowledged → resolved lifecycle. Table `Alert`. |
| **Acknowledge** | A user takes ownership of an alert without changing the environment. Requires `Technician` or `Owner`. |
| **Auto-resolve** | The system closes an alert when the metric has been inside the band (with hysteresis) for 3 consecutive minutes. |
| **Silence** | A time-boxed suppression of notifications for one metric (max 24 h), while alerts are still recorded. |
| **Exposure index** | Accumulated out-of-range magnitude, expressed in **degree-hours** for temperature (`Σ max(0, T − TargetMax) · Δt` or `Σ max(0, TargetMin − T) · Δt`), **%-hours** for humidity, and **lux-deficit hours** for light. The physically meaningful measure of how much stress the animal accumulated. |
| **Out-of-range minutes** | Count of minutes in a summary period where the metric was outside the target band. |

## Domain — platform and device

| Term | Meaning in SmartReptile |
|---|---|
| **Device / node** | The ESP32 sensor unit. Table `Device`. Identified by a stable `deviceId` (short id derived from `ChipId`). |
| **Claim code** | Short-lived (15 min), single-use 8-character code printed/shown by the device that binds it to a terrarium during onboarding. |
| **Device secret** | 256-bit random per-device credential, shown once at claim time, stored server-side as a salted hash, used as the MQTT password / HTTP bearer token. |
| **Provisioning** | The whole onboarding sequence: device creates claim code → user enters it → server binds device ↔ terrarium and issues the secret. |
| **Revoke** | Server-side invalidation of a device secret; the device is disconnected and cannot publish until re-provisioned. |
| **Telemetry** | The stream of samples from device to backend. Primary transport MQTT over TLS (`8883`), fallback HTTPS `POST /api/v1/ingest/http`. |
| **Ring buffer** | Fixed-size on-device flash/NVS buffer (≥ 12 h at 60 s) that holds samples while the network is down. |
| **Back-fill** | Re-publishing buffered samples after reconnection, flagged with quality bit `8`; ordered by `RecordedAt`, deduped by `(deviceId, seq)`. |
| **Health payload** | Non-measurement device facts: RSSI, uptime, free heap, firmware version, power source, battery %. |
| **Last seen** | Server-side `LastSeenAt` per device; drives the `Online` / `Offline` indicator. Offline = no sample for `3 × samplingInterval`. |
| **Ingest** | The server pipeline that validates, enriches, dedupes and persists a sample. |
| **Rollup** | Pre-aggregated `TelemetryHourlyRollup` row (min/max/avg/count per metric per hour) used for charts beyond 24 h and for cheap retention. |
| **Daily summary** | `DailyEnvironmentalSummary` per terrarium per local day: min/max/avg, out-of-range minutes, light hours, exposure index, alert count. The report and the v2 dataset both read from here. |

## Platform — technical terms

| Term | Meaning in SmartReptile |
|---|---|
| **MQTT** | OASIS publish/subscribe protocol used for telemetry. Topic scheme in `07-appendices/03`. |
| **LWT (Last Will and Testament)** | Retained message the broker publishes when the device disconnects unexpectedly; used to flip `device.status` to `offline` quickly. |
| **QoS 1** | At-least-once delivery for telemetry. Duplicates are expected and handled by the idempotency rule. |
| **JWT / refresh token** | User authentication pair: short-lived access token (15 min) + rotating refresh token (30 days), both `HttpOnly`-safe in the app's secure storage. |
| **RBAC** | Role-based access control: `Owner` (full), `Technician` (operate + acknowledge), `Viewer` (read-only). |
| **SignalR** | Real-time channel from backend to web dashboard and app for live values and alert badges. |
| **FCM** | Firebase Cloud Messaging — push channel to the Flutter app. |
| **RFC 7807** | Standard JSON problem-details error body used by the REST API. |
| **Idempotency-Key** | Client-supplied key on ingest and mutation requests so retries cannot create duplicates. |

## Abbreviations

| Abbr. | Expansion |
|---|---|
| ADC | Analog-to-digital converter |
| BOM | Bill of materials |
| DHT22 / SHT31 | Temperature + relative-humidity sensor families (SHT31 chosen, see ADR-014) |
| E2E | End-to-end (test) |
| EMA | Exponential moving average |
| ESP32 | Espressif dual-core Wi-Fi/BLE microcontroller |
| FCM | Firebase Cloud Messaging |
| FR / NFR | Functional / non-functional requirement |
| I²C | Inter-Integrated Circuit bus |
| LWT | MQTT Last Will and Testament |
| NVS | ESP32 non-volatile storage (flash key-value store) |
| P95 | 95th percentile |
| RBAC | Role-based access control |
| RH | Relative humidity |
| TBC | To be confirmed (open decision) |
| UVI | UV index |
