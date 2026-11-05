# smart_reptile

A new Flutter project.

## Tests

```bash
flutter test                      # widget + unit (no device)
flutter gen-l10n                  # the ARB deck is generated, not committed

# E2E: needs an Android emulator or device; the script boots one with a window if none is running.
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite smoke
```

`scripts\e2e.ps1` writes `build/e2e/<suite>.json` + `<suite>.log` and exits `0`/`1`/`2` (passed / failed /
prerequisite missing). Four suites exist: `smoke` (guest shell, no backend), `hermetic` (login, refusal,
registration, language, sign-out — against a stub API inside the test process), `fullstack` (the same journeys
against `docker compose`, fixture created and deleted through the public API) and `alerts` (a claimed board sends an
excursion through the device-facing ingest route and the alert must appear in the app — `TC-E2E-03`'s software
half). The suites and their scope are in `docs/04-quality/05-e2e-ai-testing-plan.md`.

### Watching a run

The mobile run is watchable, the way a Playwright run is: the AVD is booted with a window and the app drives itself
inside it. On top of the app, `lib/core/e2e_hud.dart` paints a banner while a case is running:

```
● E2E RUNNING · HERMETIC                                       0:12
E2E-AI-03 signing in reaches the dashboard
waiting for widget with semantics identifier "login.identifier"
```

The banner names the suite, the case and the step in flight, and turns green (`E2E PASSED`) or red (`E2E FAILED`)
when the case ends. It is inert outside a run — a keeper never sees it — and it paints with a `CustomPainter`
rather than `Text`, so it cannot add matches to the suites' own `find.text` assertions. Suites publish to it through
`integration_test/helpers/hud.dart` (`e2eCase`) and `helpers/pump.dart`, which reports every `pumpUntilFound` /
`pumpUntilGone` / `tapDestination` on its own.

```bash
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1                  # smoke + hermetic, emulator visible
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite fullstack # needs Docker Desktop running
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Headless        # unattended: no emulator window
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -RestartDevice   # a windowless AVD is still up
```

`CI` set to any value makes the run headless, which is what an unattended job wants. `.github/workflows/ci.yml` runs
`flutter test` against its own emulator rather than this script, so nothing in the pipeline depends on the window.
## Live camera (demo)

[lib/screens/camera_screen.dart](lib/screens/camera_screen.dart) shows a live feed inside the app, opened from a
terrarium's detail screen (*Camera trực tiếp*), the account tab, or the camera icon on that screen. Two transports are
selectable on the screen itself:

| Mode | How it works | When to use it |
|---|---|---|
| **WebRTC** (default) | The second phone runs `CameraPublisherScreen` (`Profile → Camera trực tiếp → Dùng máy này làm camera`), the viewer opens the same screen on the other device. The API only relays offer/answer/ICE over `/ws/camera/{terrariumId}`; video is peer-to-peer | The demo. Works in a release build, no stream URL to type, and the API never carries video |
| **URL (MJPEG)** | Any HTTP camera endpoint: IP Webcam's `http://<ip>:8080/video`, an ESP32-CAM's `/stream`, or `…/shot.jpg` polled | Fallback when the phone only runs a plain MJPEG server |

Signalling protocol (JSON over WebSocket, `?role=camera|viewer&access_token=…`):

* server → client: `welcome`, `camera-present`, `camera-joined`, `camera-left`, `viewer-joined`, `viewer-left`, `error`
* client → server: `offer`, `answer`, `ice`, `bye`; the camera adds `target: <viewerId>` to address one viewer
* the server stamps `cameraId` / `viewerId` on relayed messages so a peer can pick the right `RTCPeerConnection`
* one camera per terrarium (`camera_busy` for a second), any number of viewers; membership is checked with the same
  rule as the telemetry hub

Points that decide whether it works:

| Requirement | Note |
|---|---|
| Phone and PC on the same Wi-Fi | ICE uses host candidates; STUN `stun.l.google.com:19302` is configured for anything less direct. Two peers that cannot dial each other — an emulator, whose only IPv4 candidate is the host's public address, or two different networks — need a relay: `--dart-define=TURN_URL=turn:<host>:3478 --dart-define=TURN_USERNAME=… --dart-define=TURN_PASSWORD=…`, shared by both roles through `lib/services/ice_servers.dart` |
| Camera permission | Declared in `android/app/src/main/AndroidManifest.xml` as optional; Android asks on first publish |
| Cleartext HTTP | Debug builds allow `http://` to any address (`android/app/src/debug/res/xml/network_security_config.xml`); the release config still permits only `10.0.2.2`, `localhost`, `127.0.0.1`. WebRTC itself is not affected |
| A default MJPEG address | Optional: `--dart-define=CAMERA_URL=http://192.168.1.20:8080/video` |

### Running the live view on a USB-connected phone

A phone on the cable still needs a data path: `adb reverse` forwards a device port to the host, so the app can talk to
the API on `127.0.0.1` without knowing the PC's address, and the phone keeps its Wi-Fi for the WebRTC media path.

1. **Confirm adb sees the phone** — it must say `device`, not `unauthorized` or `offline`:

   ```powershell
   flutter devices
   adb devices
   ```

   `unauthorized` means the "Allow USB debugging" prompt on the phone was never accepted; `offline` usually means the
   USB mode is "charging only" — pick *File transfer* and reconnect.

2. **Forward the API port** (repeat after every reconnect or emulator restart; `adb reverse` does not survive either):

   ```powershell
   adb -s <serial> reverse tcp:8080 tcp:8080
   adb -s <serial> reverse --list
   ```

3. **Run the app against that forward**, passing the TURN server the emulator also uses:

   ```powershell
   cd app
   flutter run -d <serial> `
     --dart-define=API_BASE=http://127.0.0.1:8080 `
     --dart-define=TURN_URL=turn:<pc-lan-ip>:3478 `
     --dart-define=TURN_USERNAME=<user> `
     --dart-define=TURN_PASSWORD=<password>
   ```

4. **Publish from the phone**: *Terrarium → a terrarium → Camera trực tiếp* → camera icon, or *Dùng máy này làm
   camera* → **Bật camera** → grant the camera permission. The screen must read **`Đang phát tới <viewerId>`**;
   that string is only set once the peer connection reaches `connected`, so it is the check that ICE completed, not
   merely that the offer was sent. Leave this screen open — leaving it calls `dispose()` and stops the camera.

5. **Watch on a second device** (another phone, or an emulator that has `adb reverse tcp:8080 tcp:8080` applied):
   open the same screen, keep the default **WebRTC** tab, pick the same terrarium.

| Symptom on the cable | Cause |
|---|---|
| `Connection refused` on `127.0.0.1:8080` | `adb reverse` was never set, or was cleared when the device reconnected |
| `ĐANG KẾT NỐI` then `Không thiết lập được đường truyền video` | ICE finished without a usable pair — missing/incorrect `TURN_URL`, or the TURN server is not reachable from both devices |
| `Đã kết nối nhưng camera không gửi được hình ảnh` | The peer connected but never produced a frame — typically the publisher screen was left or the camera was taken by another app |
| Banner `RIGHT OVERFLOWED BY … PIXELS` | Pre-existing layout overflow on the dashboard, unrelated to the camera |
| Phone disappears from `adb devices` mid-run | The cable/USB mode dropped; reconnect, then redo step 2. A debug `flutter run` session ends with *Lost connection to device* |

This is the demo's *live view*; FR-17 itself specifies a manual snapshot only, and the backend has no camera binding,
so the URL preference is stored per app rather than per terrarium.

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.
