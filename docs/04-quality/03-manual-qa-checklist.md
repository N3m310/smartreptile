# 03 — Manual QA Checklist

Everything automated testing cannot cover: hardware, physics, real network conditions, and the demo itself.
Fill it in as you go — the completed log is a report appendix (`R14.5 QA log`).

**How to use:** run the section when its milestone is reached (`03-implementation/07`), tick the boxes,
and write the *observed* value in the "Evidence" column. An unticked box is not a failure; a ticked box with
no evidence is.

---

## 1. Bench bring-up (Milestone M1)

| # | Check | How | Pass condition | Evidence | Done |
|---|---|---|---|---|---|
| 1.1 | Board flashes and boots | `pio run -t upload` + serial monitor | Boot banner with firmware version, no reset loop | version string | ☐ |
| 1.2 | I²C bus scan finds all sensors | Address scan on GPIO 21/22 | `0x44` (SHT31), `0x23` (BH1750), `0x53` (LTR390) | address list | ☐ |
| 1.3 | 1-Wire probe found | Dallas scan | One device id printed | ROM id | ☐ |
| 1.4 | Readings are plausible at room temperature | Serial log for 60 s | 24–33 °C, 40–70 %RH, non-zero lux when lit | log extract | ☐ |
| 1.5 | Lid/cover does not bake the sensor | Compare inside vs outside the box after 10 min | Difference documented (enclosure self-heating quantified) | Δ values | ☐ |
| 1.6 | OLED shows values and the claim code | Visual | Legible, updates ≥ 1 Hz, code readable from 50 cm | photo | ☐ |
| 1.7 | Lux sensor placement check | Cover / uncover the light | Reading drops/rises within 2 s; not shaded by the sensor housing | log | ☐ |
| 1.8 | Power: mains adapter, 24 h | Powered run | No brownout resets in the log | uptime at 24 h | ☐ |
| 1.9 | Wi-Fi RSSI acceptable at the terrarium | Health payload | ≥ −75 dBm; documented location constraint | RSSI | ☐ |

**Action if 1.5 fails:** move the sensor out of the lamp's direct beam and record the offset — do not
"solve" it by tightening the band.

---

## 2. Sensor accuracy verification (Milestone M1, repeated M5)

Method: place the reference next to the sensor, let both stabilise 10 min, record 5 paired readings 1 min
apart, then compute the mean difference.

| Metric | Reference used | Mean Δ before calibration | Offset applied | Mean Δ after | Pass (±) |
|---|---|---|---|---|---|
| Temperature | *(digital thermometer, model/№)* | | | | ±0.5 °C |
| Humidity | *(hygrometer, model/№)* | | | | ±3 %RH |
| Illuminance | *(phone lux meter / dedicated meter)* | | | | ±10% |
| UV index | *(no reference available → indicative only)* | — | — | — | not claimed |
| Surface temp | *(IR thermometer)* | | | | ±1.0 °C |

Notes to record:
- Ambient conditions during the check (room temperature, whether a lamp was on).
- Whether the offsets stayed stable after a power cycle (NVS persistence).
- **A contradictory result is a finding, not a nuisance.** Example from the design review: the DS18B20 probe
  placed on the basking rock reads the lamp's radiant heat rather than the rock — that is why the placement
  rule in `07-appendices/04` §4 exists and why `GradientWarning` exists at all.

---

## 3. Functional walkthrough (Milestone M2–M4)

| # | Scenario | Steps | Pass condition | Done |
|---|---|---|---|---|
| 3.1 | Provision a fresh node | Factory reset → power → enter code in the app | Bound, `online`, first sample in the app ≤ 90 s, code unusable afterwards | ☐ |
| 3.2 | Wrong Wi-Fi password | Configure with a bad password | Device stays in provisioning mode, no claim code issued, clear feedback | ☐ |
| 3.3 | Re-claim after factory reset | Reset the node, claim again | New secret issued, old credential rejected, history preserved | ☐ |
| 3.4 | Viewer role | Create a Viewer account, log in | No ack/resolve buttons, no threshold editing, no device actions | ☐ |
| 3.5 | Technician role | Log in as Technician | Can ack/resolve/silence; cannot delete a terrarium or rotate a secret | ☐ |
| 3.6 | Threshold edit validation | Try `min 40 / max 30`, then a valid change | Blocked with field errors; valid change reflected in `effectiveThresholds` and in the card's band label | ☐ |
| 3.7 | Day/night switch | Set a short photoperiod window, watch the boundary | Band and phase label switch at the configured local time | ☐ |
| 3.8 | Silence a metric | Silence humidity 2 h, induce a humidity excursion | No notification; alert still recorded; silence visible on the dashboard; expires automatically | ☐ |
| 3.9 | Maintenance mode | Set device to Maintenance, disturb the sensor | No notifications; alerts recorded; banner visible | ☐ |
| 3.10 | Export | Export 7 days CSV + JSON | Files download, row counts match the API's `count`, open cleanly in Excel/LibreOffice | ☐ |
| 3.11 | Language switch | Switch to `en` then back to `vi` | All screens switch, no untranslated keys, layout does not break (Vietnamese strings are longer) | ☐ |
| 3.12 | Deep links | Tap a push notification | Opens the alert detail directly, back navigation sane | ☐ |
| 3.13 | Empty state | Delete all terrariums | Empty state names the next action, no crash, no misleading zero values | ☐ |

---

## 4. Accessibility and layout (Milestone M4)

| # | Check | Pass condition | Done |
|---|---|---|---|
| 4.1 | Text scale 130% | No clipped metric cards; values still legible | ☐ |
| 4.2 | Portrait 360 dp / 480 dp / 600 dp / tablet | 1 / 2 / 2 / 4 columns; no overflow stripes | ☐ |
| 4.3 | Landscape phone | Chart usable, alert banner not clipped | ☐ |
| 4.4 | Contrast | ≥ 4.5:1 body text, ≥ 3:1 large text (measured with a contrast tool) | ☐ |
| 4.5 | Colour-blind simulation (deuteranopia filter) | Status still readable via icon + label | ☐ |
| 4.6 | TalkBack | Every metric card announced as "name, value, unit, status"; buttons have labels | ☐ |
| 4.7 | Web wallboard at 1080p+ | Readable from 2 m; auto-refreshes without a manual reload | ☐ |

---

## 5. Network and reliability drills (Milestone M5)

| # | Drill | Expected | Observed | Done |
|---|---|---|---|---|
| 5.1 | Broker stopped 30 min, then restarted | Device buffers, back-fills; 0 lost samples; no duplicates; coverage ≥ 98% for the window | | ☐ |
| 5.2 | Database stopped 10 min | Ingest halts cleanly; device retries; no partial rows; `/ready` reports `db:false`; reads fail with a clear error | | ☐ |
| 5.3 | API restarted mid-soak | No gap > 1 sampling interval; evaluation resumes from `LastEvaluatedSampleId` | | ☐ |
| 5.4 | Node unplugged 10 min | `DeviceSilent` warning → critical; evaluation paused notice; auto-resolve on return; gap visible in the chart | | ☐ |
| 5.5 | Wi-Fi router restarted | Device reconnects ≤ 60 s with backoff, no manual intervention | | ☐ |
| 5.6 | Wrong device clock (NTP blocked) | `clock_unsynced` flagged; samples still stored; phase attributed by server time | | ☐ |
| 5.7 | Broker credentials wrong | Connection refused; audit entry; nothing persisted | | ☐ |
| 5.8 | Revoked device | Blocked within 60 s; cannot publish until re-provisioned | | ☐ |
| 5.9 | Phone offline for 10 min, then online | Live values refresh, no stale value displayed as current | | ☐ |
| 5.10 | 24 h soak | Coverage ≥ 99%; ≤ 1 false positive; heap stable; no reset | | ☐ |
| 5.11 | Dashboard diagnosis is not wrong | the unbuilt-endpoint case shows a *warning* that the feature is not on the server yet (`http_404`); the dead-port case shows an *error* that the server cannot be reached — never the same message | | ☐ |

---

## 6. Common-sense "keeper test" (do this before the demo, with a non-author)

| # | Question to the person looking at the screen | Pass condition | Done |
|---|---|---|---|
| 6.1 | "Is your animal okay right now?" | Answerable in < 10 s without asking how the app works | ☐ |
| 6.2 | "How long was it too hot yesterday?" | Answerable from the summary line, not by reading a chart | ☐ |
| 6.3 | "Something is wrong — what happened?" | The alert detail explains what, when, how long, and against which band | ☐ |
| 6.4 | "Is the device even working?" | Offline/stale state is obvious without interpretation | ☐ |
| 6.5 | "Where did these limits come from?" | Band source/citation is visible in the thresholds screen | ☐ |

This section is the cheapest test in the document and catches the most embarrassing problems.

---

## 7. Device matrix

| Device | OS / browser | Role in testing | Result |
|---|---|---|---|
| Android phone (primary demo) | Android 13+ | Release APK, push, live values | |
| Android emulator | API 34, 1080×2400 | Widget/integration runs | |
| Windows laptop | Chrome / Edge latest | Web dashboard, wallboard, report screenshots | |
| Windows laptop | Firefox latest | Dashboard cross-browser check | |
| Low-end Android (if available) | Android 9–10 | Performance reality check on the app | |
| ESP32 DevKitC v4 | Firmware 1.0.0 | The node under test | |

---

## 8. Bug report template

```
BUG-<nn>   Severity: S1 / S2 / S3        Found by: <name>   Date:
Environment: fw <version>, api <version>, app <version+code>, device <model>
Requirement(s): FR-xx / NFR-xx
Steps to reproduce:
  1. …
  2. …
Expected (per doc):        … (quote the doc + section)
Observed:                  …
Evidence:                  log excerpt / screenshot / SQL query result
Root cause:                …
Fix:                       commit <hash>
Regression test added:     TC-xx-<n>  (must exist before the bug is closed)
```

**Closing rule.** A bug is closed only when the regression test exists and fails on the pre-fix commit.
This is the single rule that turns a coursework project into something a reviewer takes seriously.

---

## 9. Demo-day checklist (M6)

| # | Item | Done |
|---|---|---|
| 9.1 | Backend + broker + DB running from a cold start, migrations applied | ☐ |
| 9.2 | Device online, in-range readings visible on the wallboard | ☐ |
| 9.3 | Release APK installed on the phone (release-mode screenshot already captured) | ☐ |
| 9.4 | Induced-excursion demo rehearsed: lamp/ice ready, dwell timing known (5 min warn / 2 min critical) | ☐ |
| 9.5 | Telegram channel tested within the last 30 min | ☐ |
| 9.6 | Fallback: recorded video of the alert path on a USB stick | ☐ |
| 9.7 | Seeded dataset reset script tested (restores a clean, pretty 7-day history for charts) | ☐ |
| 9.8 | Battery/power for the node, spare USB cable, phone charger | ☐ |
| 9.9 | `13`-slide summary of the report ready for the 15-minute slot | ☐ |
| 9.10 | Each member knows which section of the demo they own | ☐ |
