# 04 — Hardware: BOM, Wiring and Bring-Up

Target: one ESP32-based sensor node inside/near a 20×10 cm terrarium, mains powered, reporting over Wi-Fi.

> **Prices are indicative (VND, 2026, Vietnamese hobbyist market) and must be replaced with the actual purchase
> figures before submission** (NFR-09 verification). Record the receipt amount in the "Actual" column.

---

## 1. Bill of materials

### 1.1 Required

| # | Item | Spec | Qty | Unit (VND) | Total | Actual | Notes |
|---|---|---|---|---|---|---|---|
| 1 | ESP32 DevKitC v4 | ESP32-WROOM-32E, dual-core, 2.4 GHz Wi-Fi, 4 MB flash | 1 | 150 000 | 150 000 | | Module bay improves Wi-Fi; ESP32-WROOM-32E preferred over older ESP32-D0WD clones |
| 2 | SHT31-D breakout | I²C `0x44`, ±0.3 °C, ±2 %RH, with PTFE filter option | 1 | 120 000 | 120 000 | | **Primary temp/RH sensor** (ADR-014). Buy 2 (one spare — R-07) |
| 3 | BH1750 module | I²C `0x23`, 1–65 535 lx | 1 | 45 000 | 45 000 | | Illuminance |
| 4 | DS18B20 waterproof probe | 1-Wire, ±0.5 °C, 1 m cable | 1 | 45 000 | 45 000 | | Surface/basking-spot temperature (enables `GradientWarning`) |
| 5 | OLED 0.96" SSD1306 | I²C `0x3C`, 128×64 | 1 | 60 000 | 60 000 | | Shows live values + the claim code (provisioning UX) |
| 6 | Resistor 4.7 kΩ | ¼ W | 1 | 1 000 | 1 000 | | DS18B20 1-Wire pull-up (mandatory) |
| 7 | Jumper wires | Dupont F-F/M-F set | 1 set | 25 000 | 25 000 | | |
| 8 | Perfboard / breadboard | 5×7 cm perfboard for the final build | 1 | 30 000 | 30 000 | | Breadboard for development, soldered board for the demo |
| 9 | 5 V / 2 A USB power adapter + cable | Regulated, ≥ 2 A (Wi-Fi peaks) | 1 | 100 000 | 100 000 | | Node is mains-powered; a weak phone charger causes brownout resets |
| 10 | Terrarium box 20×10 cm | Glass/acrylic, with lid | 1 | 150 000 | 150 000 | | The model habitat (brief §4) |
| 11 | Enclosure for the electronics | ABS box ~80×60×30 mm, with vents | 1 | 40 000 | 40 000 | | Keeps the board away from humidity; vents prevent heat build-up |
| 12 | Cable glands / cable ties / double-sided tape | — | 1 set | 20 000 | 20 000 | | Probe routing into the box |
| | **Subtotal (required)** | | | | **786 000** | | Budget NFR-09: ≤ 1 500 000 ✓ |

### 1.2 Recommended / optional

| # | Item | Spec | Qty | Unit (VND) | Total | Actual | Why optional |
|---|---|---|---|---|---|---|---|
| 13 | LTR390 module | I²C `0x53`, UV index + ambient light | 1 | 120 000 | 120 000 | | UV measurement (FR-10 `UvIndex`); without it, UV is seeded as `NotMeasured` and no UV alerts exist |
| 14 | Spare SHT31 / BH1750 | as above | 1 each | 165 000 | 165 000 | | Swap-in during a demo failure (R-07) |
| 15 | ESP32-CAM (AI-Thinker) | OV2640 + separate ESP32 | 1 | 180 000 | 180 000 | | Optional FR-17 snapshot node; **not** the same board as the sensor node |
| 16 | Micro-USB/USB-C data cable (spare) | — | 1 | 30 000 | 30 000 | | Field flashing |
| | **Subtotal (optional)** | | | | **495 000** | | |

**Full build with all options ≈ 1 281 000 VND**, still under the NFR-09 ceiling, leaving headroom.
Consumables (solder, heat-shrink) are excluded as they are lab-provided.

### 1.3 Rejected components (and why)

| Rejected | Reason |
|---|---|
| DHT22 / DHT11 | ±0.5 °C/±2–5 %RH, 2 s minimum interval, less stable long-term (ADR-014) |
| BME280 | Slower thermal response in still air; pressure is irrelevant to this product |
| LDR / photoresistor | Uncalibrated, no meaningful lux value, temperature-dependent |
| ESP32-S3 / Raspberry Pi Pico W | ESP32 classic has the widest community support for this exact stack; Pico W adds an SDK with no benefit here |
| Relay module / MOSFET for a lamp | Actuation is out of scope in v1 (ADR-008, safety) |
| 3.7 V LiPo + TP4056 | Mains power is available; batteries add charging/wear failure modes without a requirement |

---

## 2. Pin map (ESP32 DevKitC v4)

| Function | ESP32 pin | Peripheral | Notes |
|---|---|---|---|
| I²C SDA | GPIO 21 | SHT31, BH1750, LTR390, SSD1306 | 4.7 kΩ pull-ups are on the breakout boards; do not add more |
| I²C SCL | GPIO 22 | same bus | |
| 1-Wire data | GPIO 4 | DS18B20 | **4.7 kΩ pull-up between data and 3V3 is mandatory** |
| OLED reset | not needed | SSD1306 | I²C modules share the bus, address `0x3C` |
| Status LED | GPIO 2 | on-board LED | Blink patterns: solid = streaming, 500 ms blink = connecting, fast blink = provisioning, off = no Wi-Fi |
| Button (optional) | GPIO 0 | on-board BOOT button | Long press (5 s) = factory reset (clears NVS secret and returns to provisioning) |
| Reserved (future) | GPIO 25/26/27 | — | Actuator outputs in v1.1 (ADR-008) |
| Do not use | GPIO 6–11 | — | Connected to the flash chip; using them bricks Wi-Fi |
| Do not use | GPIO 34–39 | — | Input-only, no internal pull-ups |

I²C addresses in use: `0x44` (SHT31), `0x23` (BH1750), `0x53` (LTR390), `0x3C` (OLED) — no conflicts.
If a BH1750 module is strapped to `0x5C`, note it in `sr_config.h`.

```
        ESP32 DevKitC v4                        SHT31-D                 BH1750            LTR390
      ┌──────────────────┐                  ┌──────────────┐        ┌──────────┐      ┌──────────┐
      │ 3V3 ─────────────┼───── 3V3 ────────┤ VIN          │────────┤ VCC      │──────┤ VCC      │
      │ GND ─────────────┼───── GND ────────┤ GND          │────────┤ GND      │──────┤ GND      │
      │ GPIO21 (SDA) ────┼───── SDA ────────┤ SDA          │────────┤ SDA      │──────┤ SDA      │
      │ GPIO22 (SCL) ────┼───── SCL ────────┤ SCL          │────────┤ SCL      │──────┤ SCL      │
      │ GPIO4  (1-Wire) ─┼──┬── DQ ────┐    └──────────────┘        └──────────┘      └──────────┘
      │                  │  │          │
      │                  │ 4.7kΩ       │  DS18B20 (waterproof probe, 1 m cable)
      │ 3V3 ─────────────┼──┴──────────┘      → placed on the substrate / basking spot
      │ GND ─────────────┼──────────────────── DS18B20 GND (red=none, black=GND, yellow=DATA)
      └──────────────────┘
```

**Wiring check before power-on:** continuity on 3V3/GND (no short), pull-up present, DS18B20 data on GPIO 4,
I²C scan lists `0x23/0x3C/0x44` (+`0x53`). A 5 V signal into an I²C line will destroy the sensor module — the
3V3 rail only.

---

## 3. Power budget

| State | Current @5 V | Note |
|---|---|---|
| ESP32 Wi-Fi TX peak | 240–350 mA | Momentary; a ≥ 2 A adapter prevents brownout |
| ESP32 average (60 s sampling, modem sleep) | 60–90 mA | `WIFI_PS_MIN_MODEM` enabled |
| SHT31 (measuring) | 0.6 mA | |
| BH1750 | 0.2 mA | |
| LTR390 | 0.6 mA | |
| DS18B20 (converting) | 1.5 mA | |
| OLED SSD1306 | 10–20 mA | Can be dimmed or disabled after provisioning to save power |
| **Total average** | **≈ 75–115 mA** | ≈ 0.4–0.6 W → ~10 kWh/year if run continuously |

No battery operation is claimed (assumption A-03). If a coin-cell/battery variant were ever wanted, deep sleep
and a longer sampling interval would be required, which conflicts with continuous 60 s monitoring.

---

## 4. Enclosure and sensor placement rules

These rules exist because wrong placement produces *confident wrong data*, which is worse than no data.

| # | Rule | Why |
|---|---|---|
| 4.1 | Put the SHT31 **in the shade**, never in the lamp's direct beam | Direct radiation raises the reading 3–8 °C and would cause false critical alerts |
| 4.2 | Mount the board so air can flow through the enclosure vents | Self-heating inside a sealed box biases temperature upward |
| 4.3 | The DS18B20 probe goes **on the substrate/basking surface**, not in the air and not under the lamp | It measures surface temperature for `GradientWarning`; radiant exposure to the lamp measures the lamp, not the rock |
| 4.4 | Keep the BH1750 facing the light source, unshaded by the enclosure lid | The light-hours metric depends on it; document any known shading |
| 4.5 | Route cables so they cannot be chewed or pulled by the animal | Obvious, and the most common real-world failure |
| 4.6 | Keep electronics **outside** the humid zone when possible | Condensation shortens the life of cheap breakouts; the probe/pigtail is the only intrusion |
| 4.7 | Do not place the node where the keeper will routinely move it | Moving the sensor changes the microclimate it measures; if it must move, note the date (it shows up as a step change in the charts) |
| 4.8 | Record the sensor positions with a photo in the report | Makes the accuracy discussion reproducible |

**Important physical sanity check (from the brief's open question TBC-2):** a 20×10 cm footprint is a *scale
model*. Confirm with the mentor whether this is the floor or the whole box; for an adult leopard gecko it would
be too small, and the report must say so rather than pretend otherwise. The system itself is size-agnostic, so
the enclosure size affects only the accuracy discussion and the photos.

---

## 5. Bring-up sequence (do this in order, checking as you go)

| Step | Action | Check |
|---|---|---|
| 1 | Wire only the ESP32 + USB, flash a blink sketch | Board enumerates as `COMx`, LED blinks |
| 2 | Add SHT31 on I²C, run an address scan | `0x44` found; readings plausible at room temperature |
| 3 | Add BH1750 | `0x23` found; cover/uncover changes the value |
| 4 | Add OLED | `0x3C` found; text legible |
| 5 | Add DS18B20 + 4.7 kΩ pull-up | ROM id printed; touch the probe → value rises |
| 6 | Add LTR390 (optional) | `0x53` found; UVI ≈ 0 indoors, rises under a UV lamp |
| 7 | Flash the full firmware, open the serial monitor | Boot banner, sensor values every 60 s, no watchdog resets |
| 8 | Provision Wi-Fi, obtain a claim code | Code displayed on OLED, countdown running |
| 9 | Claim the device in the app, first sample appears | Dashboard shows a live value within 90 s (`TC-E2E-01`) |
| 10 | Run the reference accuracy comparison | Table filled in QA §2 (before calibration) |
| 11 | Apply calibration offsets, verify persistence across a reboot | Offsets survive the power cycle |
| 12 | Place the node in the terrarium per §4, take photos | Readings plausible, `GradientWarning` not spuriously firing |
| 13 | 24 h soak | Coverage ≥ 99%, heap stable, ≤ 1 false positive |

Every step produces a small artefact (screenshot, log line, photo). Collect them as you go; the report
appendix needs them and they cannot be reconstructed later.

---

## 6. Bench mode (fake sensors)

The firmware supports a compile-time flag that swaps every sensor for a simulated one:

```ini
build_flags = -DSR_BENCH_MODE=1 -DSR_BENCH_PROFILE=semiArid
```

| Behaviour | Detail |
|---|---|
| Values | Generated from a smooth synthetic curve per the chosen profile (28 °C ± 1.5 °C day/night, RH 35% ± 5, lux following the photoperiod) |
| Fault injection | `-DSR_BENCH_FAULT=humidity` makes the humidity sensor fail after 10 min, to exercise `SensorFault` |
| Excursion injection | `-DSR_BENCH_EXCURSION=+6C@300s:600s` raises temperature 6 °C for 5 minutes, to exercise the dwell/escalation path on demand |

Why it exists:
1. It lets software development continue when the hardware is unavailable or broken (R-07 contingency).
2. It makes alert-path demos **repeatable** — a controlled 5-minute excursion every time, without heating
   anything (useful for `TC-E2E-03` when the room temperature is uncooperative).
3. It demonstrates to the reviewer that the alert logic is testable independently of the sensors.

Bench mode is never used for the accuracy claims and is disabled in the release firmware
(`-DSR_BENCH_MODE=0`), which is stated in the report.

---

## 7. Safety and care

| # | Note |
|---|---|
| 7.1 | Mains adapter only, no mains switching in v1 (no relays) — a student project should not switch 220 V |
| 7.2 | Keep the electronic board out of the animal's reach; tape or route cables so they cannot be pulled |
| 7.3 | The heat lamp used to *induce* a demo excursion is the keeper's own equipment and must be monitored manually — the system is monitoring-only and will not switch it off |
| 7.4 | The animal's welfare takes priority over the demo: never leave the terrarium in an extreme state for the sake of a screenshot. Use bench mode for extremes |
| 7.5 | Do not place the DS18B20 probe where the animal can bite it; use a cable gland/tie |
