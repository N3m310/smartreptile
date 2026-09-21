# SmartReptile firmware (ESP32 sensor node)

Monitoring only: the node reads sensors, filters, buffers and publishes. It holds **no thresholds** and makes
**no alert decisions** — that is deliberate (ADR-005), so bands can be changed without reflashing.

## Layout

| Path | Purpose |
|---|---|
| `platformio.ini` | Two environments: `esp32dev` (real node, pinned platform + libraries) and `native` (host tests) |
| `include/sr_config.h` | Pin map, intervals, payload budget, quality flags, plausibility bounds (prefixed `sr_` so the ESP32 framework's own `config.h` cannot shadow it) |
| `lib/firmware_core/` | Pure C++ with no Arduino headers: `filters`, `ring_buffer`, `payload` — the host-tested part |
| `src/main.cpp` | M1 skeleton: boot logging, status LED, 1 Hz tick, ring-buffer writes. Sensor drivers and transport land in M2 |
| `test/` | Unity tests for the native environment |

## Commands

```bash
pio test -e native        # host unit tests — 22 cases, milliseconds, no hardware
pio run -e esp32dev       # build for the board
pio run -e esp32dev -t upload
pio device monitor -b 115200
pio run -t size           # flash/RAM figures for the report (§05-release/02 §5)
```

## Prerequisite: a host C++ toolchain for `pio test -e native`

PlatformIO's `native` environment compiles with the **host** compiler, so it needs `g++` (or `clang++`) on the
machine. On Windows that is *not* installed by default:

- **Windows:** install MSYS2/MinGW-w64 (`winget install MSYS2.MSYS2`, then `pacman -S mingw-w64-ucrt-x86_64-gcc`)
  or LLVM (`winget install LLVM.LLVM`), and make sure `g++.exe` is on `PATH`. WSL works too if `build-essential`
  is installed there.
- **Linux/macOS:** `build-essential` / Xcode command line tools. Nothing else to do.

If no host compiler is present, `pio test -e native` fails with `'g++' is not recognized…` — the firmware code
is fine, the toolchain is missing. CI runs these tests on `ubuntu-latest`, which has `g++` preinstalled, so the
gate is enforced there in any case.

## What the tests cover

| Suite | Cases | Guards against |
|---|---|---|
| `test_filters` | 8 | A single I²C glitch becoming a phantom alert; an EMA that oscillates instead of converging |
| `test_ringbuffer` | 6 | Silent data loss and reordering; a corrupted NVS blob after a reset (TC-U-FW-04…06) |
| `test_payload` | 8 | Firmware and server disagreeing about keys, plausibility bounds or the `t` offset (TC-U-FW-03/07…09) |

## Bench mode

`build_flags = -DSR_BENCH_MODE=1` replaces the sensors with a synthetic profile so the whole server-side alert
path can be demonstrated repeatably without heating anything, and so development can continue when a sensor is
dead (`docs/07-appendices/04-hardware-bom-and-wiring.md` §6). Bench mode is **off** in any build that leaves
this machine (§05-release/01 §2 checklist).
