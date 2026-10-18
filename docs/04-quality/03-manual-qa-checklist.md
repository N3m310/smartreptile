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
| 3.4 | Viewer role | As the administrator, create an account on the `Viewer` role (`POST /api/v1/users`), then sign in as that account | No ack/resolve buttons, no threshold editing, no device actions | ☐ |
| 3.5 | Technician role | Sign in as an account the administrator put on the `Technician` role (created the same way as 3.4) | Can ack/resolve/silence; cannot delete a terrarium or rotate a secret | ☐ |
| 3.6 | Threshold edit validation | Try `min 40 / max 30`, then a valid change | Blocked with field errors; valid change reflected in `effectiveThresholds` and in the card's band label | ☐ |
| 3.7 | Day/night switch | Set a short photoperiod window, watch the boundary | Band and phase label switch at the configured local time | ☐ |
| 3.8 | Silence a metric | Silence humidity 2 h, induce a humidity excursion | No notification; alert still recorded; silence visible on the dashboard; expires automatically | ☐ |
| 3.9 | Maintenance mode | Set device to Maintenance, disturb the sensor | No notifications; alerts recorded; banner visible | ☐ |
| 3.10 | Export | Export 7 days CSV + JSON | Files download, row counts match the API's `count`, open cleanly in Excel/LibreOffice | ☐ |
| 3.11 | Language switch | Switch to `en` then back to `vi` | All screens switch, no untranslated keys, layout does not break (Vietnamese strings are longer) | ☐ |
| 3.12 | Deep links | Tap a push notification | Opens the alert detail directly, back navigation sane | ☐ |
| 3.13 | Empty state | Delete all terrariums | Empty state names the next action, no crash, no misleading zero values | ☐ |
| 3.14 | Change password | Sign in, then sign in once more from a second browser profile so there is another session to lose; use the header's *Đổi mật khẩu* / *Change password* form — first with a wrong current password, then with the right one | The wrong one is refused by the sentence that names it — *Mật khẩu hiện tại không đúng.* / *The current password is wrong.* (not the sign-in sentence, which is about a username this form does not have) — and **the session survives**: no sign-out, no `POST /auth/refresh`, the rest of the app still works, and the keeper can simply retype. The right one is confirmed in place (*Đã đổi mật khẩu. Các phiên đăng nhập khác đã kết thúc.*), **this** session keeps working, the second one is refused at its next call, the new password signs in and the old one does not | ☐ |
| 3.15 | Password rule (`BR-01.2`) | In the same form, submit a new password with no upper-case letter, then one with no special character, then one of 7 characters, then a compliant one | Each refusal **names the rule it broke** (not just "refused") — and the form states the rule above the field in both languages; the compliant one is accepted and signs in afterwards | ☐ |
| 3.16 | Species-profile catalogue (FR-10) | Open *Hồ sơ loài* / *Species profiles* **signed in as the system administrator** (`ADMIN_USERNAME`; an `Owner` sees the list and no write control at all, which is case 3.17): read a built-in, duplicate it, edit the copy (move a band, add a band, clear the note), create one from scratch, then delete the copy; finally assign a terrarium to a custom profile and try to delete that one | A built-in shows only *Nhân bản* / *Duplicate* (and the API answers `403 builtin_immutable` if the route is called anyway); every card states its band count and each band's metric, phase, target, critical, dwell and recovery; an incoherent band (`tối thiểu 30` > `tối đa 25`) is refused with the sentence *Tối thiểu phải nhỏ hơn tối đa.* marked at the input that is wrong; clearing the note or the scientific name **clears it**; a copy appears beside its source with the same bands; deleting a profile a terrarium uses is refused with *Đang có bể nuôi dùng hồ sơ này nên chưa xoá được.* rather than silently doing nothing; and a Viewer sees the list with **no** create button and no per-card write control | ☐ |
| 3.17 | The catalogue is administrator-only (FR-10, `02-design/06` §3) | Sign in as an ordinary keeper (`Owner`), open *Hồ sơ loài* / *Species profiles*, and `curl -X POST /api/v1/species-profiles` with that keeper's token; then sign in as the administrator and repeat the create | The keeper's page renders the four built-ins and their bands with **no** *Hồ sơ mới* button and no per-card write control, and the crafted call answers `403 insufficient_role` (never `404` or `500`) with the stored catalogue unchanged; the administrator's create answers `200` and the profile appears in both accounts' lists, since the catalogue is shared | ☐ |
| 3.18 | Update a terrarium from Swagger (FR-03) | Open *PATCH /api/v1/terrariums/{id}* in Swagger UI next to *GET /api/v1/terrariums/{id}*; copy the `rowVersion` the `GET` body carries into the **`If-Match`** box and send a change; then repeat with the value the first call replaced, and finally with the box left empty | The route shows an `If-Match` input box whose description says where the value comes from; the `GET` body carries `rowVersion`, equal to that response's `ETag` header, while the *Terrariums* list carries no `rowVersion` at all; the update answers `200` with a **new** `rowVersion` (and the new `ETag`); the replaced value answers `412` and the empty box `428`, each naming `GET /api/v1/terrariums/{id}`; neither refusal changed the terrarium | ☐ |
| 3.19 | A species profile's picture (`BR-10.6`) | Signed in as the administrator, open *Hồ sơ loài* / *Species profiles*; create a profile choosing a photograph in the *Hình ảnh* / *Picture* section; then edit it and choose a different one; then choose a `.txt` and save; finally press *Xoá hình* / *Remove picture* | The section shows the chosen file's name and its preview before saving; the create lands and the new card shows the picture (fetched, not an empty frame), with the profile's other fields as typed; a replacement shows the **new** picture — the old file is gone from `Storage:RootPath`; the `.txt` is refused with a sentence at the picker and the field edits still saved; *Remove picture* clears the picture from the card and from the form, and pressing it on a profile that has none is not an error; a built-in offers no picture control at all; and a picture is never shown for a profile that has none | ☐ |

| 3.20 | Ownership answers three ways (FR-03, `BR-02.2`, TASKS 2026-10-14 item 3) | Signed in as keeper A, create an enclosure and copy its id; sign in as keeper B and try `GET /api/v1/terrariums/{A's id}`, then `GET` an id that belongs to nobody, then (as A) delete the enclosure and `GET` it again | Three different answers, and none of them leaks the row: `403 forbidden_terrarium`, `404 not_found`, `410 terrarium_deleted`. Every terrarium sub-route (readings, coverage, thresholds, latest) answers the same way, and B's own list does not show A's enclosure. Then check that the same B-vs-A pair on `/alerts` and `/devices` still answers `404` for A's resource — that difference is the recorded narrowing of `BR-02.2`, not a bug | ☐ |
| 3.21 | The administrator reaches another account's enclosure (TASKS item 5) | Signed in as the administrator, open *Bể nuôi* / *Terrariums* and edit an enclosure belonging to another keeper (the list names its owner); then `curl -X POST /api/v1/terrariums` as that keeper and `GET /api/v1/audit?entity=Terrarium` as the keeper who owns the row | The administrator's list shows every account's enclosures with `ownerUserId`, the edit succeeds and answers a new `rowVersion`, and the audit trail carries `terrarium.updated` with the administrator as the actor and the owner in the payload. Deleting one answers `terrarium.deleted`, and a keeper editing their **own** enclosure still writes no audit row | ☐ |
| 3.22 | Pairing a board by its MAC (FR-04, TASKS item 8) | `POST /api/v1/devices/self-register` with a chip id and a MAC, then `POST /api/v1/devices/claim-by-mac` with the same address typed as `aa-bb-cc-dd-ee-ff`; then repeat the call for an unknown address, for the board that is now bound, and for a revoked one | The first call answers the secret once, the same body `claim` would return, and the audit row says `"method":"mac_address"`; the three later calls answer the **same** `404 device_not_found` and the same sentence, so the route cannot be used to test which addresses exist; a `MAC` that is not twelve hex digits answers `400 registration_invalid` with `errors[0].field = "macAddress"`; and `POST /devices/claim` still works for a board that has a code | ☐ |
| 3.23 | Creating an enclosure with a board (TASKS item 2) | `POST /api/v1/terrariums` with `deviceId` set to a registered board's public id; then the same call with both `deviceId` and `macAddress`; then with an id no board has; then with none at all | The first creates the enclosure **and** binds the board, answering `deviceSecret` exactly once and a `device` summary on the item; the second answers `400 validation_failed` with `device_selector_ambiguous`; the third answers `device_not_found` and **creates nothing** (the list does not grow); the fourth behaves exactly as it always did, without `deviceSecret` | ☐ |
| 3.24 | Editing and removing a board (TASKS item 9b) | `GET /api/v1/devices`, `GET /api/v1/devices/{id}`, `PATCH` with `{"deviceName": "Shed node", "samplingIntervalSec": 30}`, a `PATCH` with `samplingIntervalSec: 5`, then `DELETE /api/v1/devices/{id}` | The list shows the caller's boards and no one else's, with both `status` and the derived `runtime` badge; the `PATCH` answers `200` and a later `GET` still shows the new name and interval; the `5` is refused with `interval_out_of_range` naming the field; the `DELETE` answers the board **as revoked and unbound** (not an empty body), the enclosure's device card disappears, and the readings and audit rows that name the board are still in the database — it is not a row delete | ☐ |
| 3.25 | Account preferences and self-service deletion (TASKS item 9a) | Signed in as any keeper, `PATCH /api/v1/auth/me` with `{"preferredLanguage":"en","timeZoneId":"Europe/Paris","quietHoursStart":"22:00","quietHoursEnd":"07:00","minNotifySeverity":"critical"}`; then with `quietHoursStart` alone; then with `{"timeZoneId":"Mars/Olympus_Mons"}`; then `DELETE /api/v1/auth/me` and try to sign in | The first answers the updated profile and a later `GET /api/v1/auth/me` still shows it; the second answers `400 profile_invalid` with `quiet_hours_incomplete`; the third answers `timezone_invalid` and names `GET /api/v1/time-zones`; the `DELETE` answers `204`, every session of the account is refused at its next call, and the sign-in attempt fails while the account's terrariums and history remain | ☐ |
| 3.26 | The administrator manages accounts (TASKS item 12) | As the administrator, `GET /api/v1/users?search=…`, `POST /api/v1/users`, `GET /api/v1/users/{id}`, `PATCH /api/v1/users/{id}`, `POST /api/v1/users/{id}/reset-password`, `PATCH /api/v1/users/{id}/role`, `POST /api/v1/users/{id}/disable`, `POST /api/v1/users/{id}/enable` and `DELETE /api/v1/users/{id}`, then the same id again with `?purge=true`; then the role and disable calls against the caller's **own** id, and a disable of the last usable administrator | The list pages with `total` and a search matches username or email case-insensitively, each entry carrying the role's code and the permission codes it grants; a create without `role` lands on `Owner`, and a taken identifier answers `400 account_invalid` naming the field; a role change answers the updated account, ends that account's sessions, and `GET /api/v1/audit?entity=User` shows `account.role_changed` with the code before and after; disabling ends that account's sessions at once and re-enabling leaves them ended, so it signs in again; `DELETE` without `?purge=true` disables and keeps every row that names the account, `?purge=true` on an account enclosures, boards or alerts still reference answers `409 account_in_use` naming what does, and on a clean one it erases the row; the self-directed calls answer `409 self_change_refused` and the last-administrator one `409 last_administrator_refused`, with the stored role and state unchanged | ☐ |
| 3.27 | The time-zone catalogue (TASKS item 11) | `GET /api/v1/time-zones` signed in as any keeper; then create an enclosure with `timeZoneId` copied from a row; then with `asia/ho_chi_minh`; then with an id no row carries | The list is ordered west to east, carries `ianaId`, `displayName`, `countryCode`, `utcOffsetMinutes` and a formatted `utcOffset`, and includes `Asia/Ho_Chi_Minh` at `UTC+07:00`; the created enclosure shows the catalogue's spelling in both cases; the unknown id answers `terrarium_timezone_invalid`, whose sentence names the list route | ☐ |
| 3.28 | The role matrix is data (`02-design/06` §3) | As the administrator, `GET /api/v1/admin/roles` and `GET /api/v1/admin/permissions`; create a role with `POST /api/v1/admin/roles` that grants `alert.read` alone, put an account on it with `PATCH /api/v1/users/{id}/role` and sign in as that account; then `PATCH /api/v1/admin/roles/{id}` to drop `alert.read`; then `DELETE` a shipped role, `DELETE` the custom role while that account still holds it, and finally `DELETE` it after moving the account off it | The list carries each role's permission codes, whether the product ships it, how many accounts hold it and `crossAccountPermissions`; the catalogue lists the sixteen codes with their group, label and reach; the new role authorises through the same policies with no code change, so the signed-in account reads alerts before the edit and is refused `403 insufficient_role` after it — **not** at once: its existing access token keeps the code until it expires, and only the next `POST /api/v1/auth/refresh` returns a token without it, which is the one-lifetime delay the console warns about; a shipped role answers `409 system_role_immutable`, a role accounts still hold `409 role_in_use`, and every write lands as `role.created`/`role.updated`/`role.deleted` — the edit carrying the permission set before and after | ☐ |
| 3.29 | The two alert families | `GET /api/v1/alerts?family=enclosure` and `?family=species` as a keeper with alerts of both kinds open, then `?family=reptile`, then `GET /api/v1/alerts/{id}` and the `alertChanged` payload of a fresh alert | Each row and the detail carry `family`; the first two calls return only that family's rows — `species` for the temperature, UV and surface-basking alerts, `enclosure` for humidity, light and the board's own (silence, sensor fault, clock skew), including every alert with no metric at all; the misspelt key answers `400 family_invalid` naming `enclosure` and `species` rather than quietly returning both | ☐ |

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
| 5.12 | Web serving layout (**BUG-03**, task 4.12; **re-specified by `ADR-019`/task 4.19**) | **Until 4.19:** `http://127.0.0.1:8081/` serves a working page rather than the prototype's blank Vite entry, `/legacy/wallboard.html` + `/legacy/health.html` return `200`, and whatever `/` serves, the mock-data prototype is labelled as such. **After 4.19:** `/` serves the wired TERRAGUARD app, `/wallboard` and a deep route both return `200` through the SPA fallback, and `/legacy/*` is expected to `404` because those files are gone — a `200` there means the retired mount is still in place | | ☐ |

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
| 9.5 | Notification channel tested within the last 30 min (in-app inbox, plus FCM to the phone; SMTP only if a relay is up) | ☐ |
| 9.6 | Fallback: recorded video of the alert path on a USB stick | ☐ |
| 9.7 | Seeded dataset reset script tested (restores a clean, pretty 7-day history for charts) | ☐ |
| 9.8 | Battery/power for the node, spare USB cable, phone charger | ☐ |
| 9.9 | `13`-slide summary of the report ready for the 15-minute slot | ☐ |
| 9.10 | Each member knows which section of the demo they own | ☐ |
