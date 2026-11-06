# smart_reptile

A new Flutter project.

## Tests

```bash
flutter test                      # widget + unit (no device)
flutter gen-l10n                  # the ARB deck is generated, not committed

# E2E: needs the local API and an Android emulator or device; also writes both reports
dotnet run --project ../backend/src/SmartReptile.Api
.\tool\run_e2e.cmd                # builds both reports, then opens the dashboard
```

A double-clickable `e2e.bat` at the repository root does the same from any directory.

`integration_test/app_e2e_test.dart` is the only end-to-end suite: it drives the real app against the real API
(login, registration, navigation, terrarium CRUD, history, alerts, species, password validation) and tears its own
fixtures down. Its requirements — backend, emulator, `--dart-define=API_BASE=…` — are in
[integration_test/README.md](integration_test/README.md), and the last run is recorded in
[integration_test/TEST-RESULTS.md](integration_test/TEST-RESULTS.md), which the suite regenerates on every run from
`--file-reporter json` (see that README's **Report** section).

`flutter test` must not pick it up: `integration_test` needs a device, so the suite is run by name rather than by
directory.

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
| Screen stays awake | The publisher holds a wakelock (`wakelock_plus`) while it streams. Without it Android freezes the backgrounded app ~30 s in, the signalling socket closes, and every viewer keeps the last frame it decoded — the "camera runs a moment then stops" report |
| Cleartext HTTP | Debug builds allow `http://` to any address (`android/app/src/debug/res/xml/network_security_config.xml`); the release config still permits only `10.0.2.2`, `localhost`, `127.0.0.1`. WebRTC itself is not affected |
| A default MJPEG address | Optional: `--dart-define=CAMERA_URL=http://192.168.1.20:8080/video` |

### Running the live view on a USB-connected phone

A phone on the cable still needs a data path: `adb reverse` forwards a device port to the host, so the app can talk to
the API on `127.0.0.1` without knowing the PC's address, and the phone keeps its Wi-Fi for the WebRTC media path.

All five steps are automated by [`../setup-mobile.ps1`](../setup-mobile.ps1) — `.\setup-mobile.ps1 -StartEmulator
-Run phone` for the publishing phone, `-Run emulator` (or `-Run both`) for the viewer. The steps below are what it
does, and stay the reference when it fails.

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
| Picture plays ~30 s then freezes, no error text | The publishing phone slept (or the cable came out), its signalling socket closed, and the viewer keeps the last decoded frame — `onConnectionChange CLOSED` in `adb logcat --pid=$(pidof com.smartreptile.smart_reptile)`. The publisher now holds a wakelock while streaming; a crop of this on the emulator is `c2.goldfish.h264.decoder` being torn down and rebuilt |

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
