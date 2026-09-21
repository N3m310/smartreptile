# SmartReptile firmware (ESP32 sensor node)

Monitoring only: the node reads sensors, filters, buffers and publishes. It holds **no thresholds** and makes
**no alert decisions** — that is deliberate (ADR-005), so bands can be changed without reflashing.

## Layout

| Path | Purpose |
|---|---|
| `platformio.ini` | Two environments: `esp32dev` (real node, pinned platform + libraries) and `native` (host tests) |
| `include/sr_config.h` | Pin map, intervals, payload budget, plausibility bounds (prefixed `sr_` so the ESP32 framework's own `config.h` cannot shadow it). Quality-flag names live in `lib/firmware_core/payload.h` instead, so firmware and tests share one definition |
| `lib/firmware_core/` | Pure C++ with no Arduino headers: `filters`, `ring_buffer`, `payload` — the host-tested part |
| `src/main.cpp` | M1 skeleton: boot logging, status LED, 1 Hz tick, ring-buffer writes. Sensor drivers and transport land in M2 |
| `test/` | Unity tests for the native environment |

## Commands

```bash
pio test -e native        # host unit tests — 22 cases, ~20 s, no hardware. Needs a host compiler: see below
pio run -e esp32dev       # build for the board
pio run -e esp32dev -t upload
pio device monitor -b 115200
pio run -t size           # flash/RAM figures for the report (§05-release/02 §5)
```

## Running the host tests (this machine has no host compiler)

PlatformIO's `native` environment compiles with the **host** compiler, and the M1 development machine has none: no
MinGW, no MSVC C++ workload, no WSL `build-essential`, and PlatformIO publishes no `toolchain-gccnative` package
for Windows. Without a container, the 22 cases below cannot be run while developing — only in CI, which is the same
as not running them.

`Dockerfile.host-tests` exists to close that gap:

```bash
# from the repository root — build once, then about 20 seconds per run
docker build -f firmware/Dockerfile.host-tests -t smartreptile-fw-test firmware
docker run --rm -v "$PWD/firmware:/firmware" smartreptile-fw-test        # PowerShell: "${PWD}/firmware:/firmware"
```

It is plain Debian + `g++` + PlatformIO — the same toolchain `ubuntu-latest` gives CI — so a green run here means
what a green run there means. The source is bind-mounted, so editing a test needs no rebuild.

**Verified 2026-09-21: `22 test cases: 22 succeeded`.** That first execution earned its keep — it failed
`test_payload_budget_allows_a_backfill_batch`, exposing a real defect: the budget guard estimated 5000 bytes for
the 5 × 20 batch that `SR_BACKFILL_BATCH_MAX` is documented to keep *under* the 4 KB budget, so the firmware
would have refused the very batch its own configuration told it to send. The estimate is now calibrated against
the measured §3.2 payload (see `payload.cpp`) and the test asserts against the configured constants rather than
copies of them.

If you would rather have a real local toolchain than a container:

- **Windows:** `winget install MSYS2.MSYS2`, then `pacman -S mingw-w64-ucrt-x86_64-gcc`, and put `g++.exe` on
  `PATH` — plain `pio test -e native` then works.
- **Linux/macOS:** `build-essential` / Xcode command line tools. Nothing else to do.

## What the tests cover

| Suite | Cases | Guards against |
|---|---|---|
| `test_filters` | 8 | A single I²C glitch becoming a phantom alert; an EMA that oscillates instead of converging |
| `test_ringbuffer` | 6 | Silent data loss and reordering; a corrupted NVS blob after a reset (TC-U-FW-04…06) |
| `test_payload` | 8 | Firmware and server disagreeing about keys, plausibility bounds or the `t` offset (TC-U-FW-03/07…09), and a batch whose size silently exceeds the 4 KB payload budget (rule V-01) |

## Bench mode

`build_flags = -DSR_BENCH_MODE=1` replaces the sensors with a synthetic profile so the whole server-side alert
path can be demonstrated repeatably without heating anything, and so development can continue when a sensor is
dead (`docs/07-appendices/04-hardware-bom-and-wiring.md` §6). Bench mode is **off** in any build that leaves
this machine (§05-release/01 §2 checklist).
