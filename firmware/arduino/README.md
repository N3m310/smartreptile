# Arduino sketch — demo node (DHT11 + LM35 + BH1750)

`esp32_smartreptile/esp32_smartreptile.ino` is the single-file sketch that runs on the classroom demo board. It is
**standalone**: open it in the Arduino IDE (or `arduino-cli`) — it is not built by `firmware/platformio.ini`,
whose `src/main.cpp` is the reference implementation with host-tested logic.

The sketch does the minimum the demo needs: read the three sensors, stamp the sample with NTP time, publish over
MQTT `sr/v1/d/<deviceId>/telemetry` and fall back to `POST /api/v1/ingest/http` when the broker is unreachable.

## No secret is in the source

Wi-Fi credentials and the device secret are **never compiled in**. Both live in the board's NVS and are entered
over the Serial Monitor at **115200 baud**:

| Command | Effect |
|---|---|
| `setwifi <ssid> <password>` | Store Wi-Fi and reconnect on the next loop |
| `set <deviceId> <secret>` | Store the device credential |
| `show` | Print Wi-Fi SSID, `deviceId`, secret **length** and the current sequence — never the secret itself |

`<ssid>`/`<password>` are split at the first space, so neither may contain a space.

Where the credential comes from: `POST /api/v1/devices/claim` or `POST /api/v1/devices/claim-by-mac` returns
`deviceId` + `secret` **exactly once** (only the hash is stored). Lost it? `POST /api/v1/devices/{id}/rotate-secret`
issues a new one; the board keeps working through the rotation grace window, then needs the new secret via `set`.

A board with no secret does not transmit at all — `loop()` stops and asks for one, instead of collecting 401s.

## Pins

| Signal | ESP32 pin |
|---|---|
| DHT11 data | GPIO 4 |
| LM35 out | GPIO 34 (ADC1, 11 dB attenuation) |
| BH1750 SDA / SCL | GPIO 21 / GPIO 22 |

## Arduino IDE setup

1. Board: **ESP32 Dev Module** (Arduino-ESP32 core 2.x or 3.x — the sketch keeps the `ADC_11db` spelling so it
   builds on both).
2. Libraries (Library Manager):
   - `PubSubClient` (knolleary) 2.8
   - `BH1750` (claws) 1.3
   - `DHT sensor library` (Adafruit) 1.4 — and its dependency `Adafruit Unified Sensor`
3. Set `SR_SERVER_IP` to the LAN IPv4 of the machine running the backend (`ipconfig` → IPv4 Address).
4. Flash, open the Serial Monitor at 115200 and run `setwifi` then `set` once; both survive a reset.

## Transport notes

- `SR_MQTT_TLS 0` uses plaintext MQTT on 1883, which the backend only accepts from the LAN when it is started with
  `Mqtt__PlaintextHost=0.0.0.0`. Otherwise the sketch still delivers through the HTTPS fallback.
- HTTP rate limit is 6 requests/minute/device and `SAMPLE_INTERVAL_MS` is 12 s, i.e. 5/minute.
- A 401 means the credential was refused: the sketch keeps its sequence number, so the same sample is sent again
  after the secret is corrected. A 4xx about the payload drops the sample; a 5xx keeps it for the next round.

Compile-verified against `espressif32@6.13.0` / `board = esp32dev` (Arduino-ESP32 core bundled with that platform).
