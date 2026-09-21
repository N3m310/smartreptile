# 06 — Threshold, Species-Profile and Summary Logic

The rules that decide *when a number becomes an alert* and *what a day of data means*. This document is
the single place where the maths lives; `07-appendices/05` supplies the numbers with their sources.

## 1. Profile → terrarium resolution

```
effective(terrarium, metric, phase):
    1. ThresholdOverride  where TerrariumId = T and MetricId = M and Phase ∈ {phase, Any}
    2. Threshold          where SpeciesProfileId = T.Profile and MetricId = M and Phase ∈ {phase, Any}
    3. SystemDefault      hard-coded last resort (documented, warned about in the UI)

phase resolution:
    if any applicable row has Phase = Any → evaluate with Phase = Any   (no day/night split)
    else Day  when localTime ∈ [LightsOn, LightsOn + PhotoperiodHours)
    else Night
```

Precedence within one layer: an exact-phase row beats an `Any`-phase row; an override beats a profile row
even if the profile row is phase-specific (the UI states this in the editor).

`effectiveThresholds` API response carries `source` per metric (`override` | `profile` | `default`) — so
"why is my limit 32 and not 30?" is answerable in the app rather than in a debugger (BR-10.3).

**Change semantics (UC-03 A4).** On any change, the server writes a `ThresholdSnapshot` with the new
effective set. An alert that is already open keeps its original `BandMin/BandMax` for the record; the next
sample is evaluated against the new band. If it is now in range, the normal recovery path resolves the
alert with reason `Recovered` — no special "threshold changed" resolution exists, because inventing one
would create a fourth alert outcome that the data does not support.

## 2. Band arithmetic (validation rules)

| Rule | Formula | Rejected example |
|---|---|---|
| Ordering | `TargetMin < TargetMax` | `30 / 40` inverted → `400` |
| Critical encloses target | `CriticalMin ≤ TargetMin` and `CriticalMax ≥ TargetMax` | critical `28–31` with target `26–32` → `400` |
| Critical ordered | `CriticalMin < CriticalMax` | reversed |
| Dwell | `DwellCritMinutes ≤ DwellWarnMinutes` | critical slower to fire than warning would be incoherent |
| Recovery margin fits | `RecoveryMargin < (TargetMax − TargetMin) / 2` | margin larger than half the band would make recovery impossible |
| Phase sanity | night band must not be *hotter* than day band for temperature | warning (non-blocking): a warmer night is almost always a data-entry mistake |
| Climate-zone plausibility | within the zone's `[plausibleMin, plausibleMax]` from `07-appendices/05` | "desert" profile with `TargetMax < 20 °C` → non-blocking warning (BR-10.5) |

## 3. Worked evaluation examples

All examples use the seeded **leopard gecko** profile (target 26–32 °C, critical 22–34.5 °C, dwell 5/2,
recovery 0.5 °C) and a 60 s sampling interval.

### Example A — sub-dwell spike produces nothing

| Minute | Temp | State |
|---|---|---|
| 14:00 | 31.2 | in band |
| 14:01 | 32.4 | violation starts, `FirstOutOfBandAt = 14:01` |
| 14:02 | 32.9 | noise (lid opened) |
| 14:03 | 32.1 | still out |
| 14:04 | 31.0 | back in band → recovery tick 1 |
| 14:05 | 30.8 | recovery tick 2 |

Sustained time never reaches 5 minutes → **no alert**. This is the single most valuable behaviour in the
system for a keeper: a lid opened for four minutes does not wake anyone up.

### Example B — a real excursion opens exactly one alert

| Minute | Temp | State |
|---|---|---|
| 14:00 | 31.8 | in band |
| 14:01 | 32.3 | violation starts (14:01) |
| 14:02–14:05 | 32.4, 32.6, 32.5, 33.0 | still out, sustained = 5 min at 14:06 check |
| 14:06 | 33.4 | **Open Warning**, `TriggeredAt = 14:01`, message "out of range for 5 min" |
| 14:07–14:20 | 33.5 … 34.8 | `Touch` updates `LastObservedAt`, `PeakValue` |
| 14:21 | 34.9 | crosses critical 34.5 → `CriticalSinceAt = 14:21` |
| 14:23 | 35.0 | critical sustained 2 min → **Escalate**, same alert id, severity Critical, one new push |
| 14:30 | 34.9 | still critical, no new alert (dedupe key open) |
| 14:41 | 33.9 | inside critical, outside target → still Warning-level violation, alert stays open |
| 14:52 | 31.5 | inside band by ≥ 0.5 → recovery tick 1 |
| 14:53, 14:54 | 31.4, 31.6 | recovery ticks 2, 3 → **Resolved** (reason `Recovered`) |

One alert row, one warning push, one critical push, one recovery notice. History shows
`TriggeredAt 14:01`, `ResolvedAt 14:54`, `PeakValue 35.0`.

### Example C — night phase prevents a false alarm

Profile: day 26–32 °C, night 22–27 °C, photoperiod 07:00 + 12 h → night from 19:00.
At 20:30 the terrarium reads **24.5 °C**. Against the day band this would be *below* target (an alert), but
the profile's night band makes it **in range**. A drop at night is physiology, not a fault — the reason
day/night bands exist at all (FR-10, `07-appendices/05` §3).

### Example D — device silence is not a habitat alert

Sampling 60 s; last sample 14:00. At 14:04 the watchdog sees `3 × 60 s` without data → **`DeviceSilent`
Warning**. No temperature/humidity alerts are raised from missing data (there is no data), and the dashboard
states that evaluation is paused for this terrarium. At 14:30 the alert escalates to Critical. On reconnect
at 15:10 with 70 buffered samples, back-fill is ingested, excursions older than 6 h would not notify anyway
here, and the alert auto-resolves.

## 4. Light and UV logic (different from temperature, on purpose)

| Metric | Band style | Why |
|---|---|---|
| `LightLux` | **Not a per-sample alert by default.** The profile defines `lightThresholdLux` (e.g. 1 000 lx) and `minLightHoursPerDay` (e.g. 8 h), and the rule is *accumulated* (`LightDeficit`), evaluated at 21:00 local | A shadow from a plant or a passing cloud is not an incident; a failed lamp timer all day is |
| `UvIndex` | Banded like temperature, but with a long dwell (default 15 min) and typically only an **upper** bound warning plus a lower bound "below target" info | UV sensors are indicative, and UVI naturally fluctuates with the lamp's warm-up |
| `SurfaceTempC` | Banded, plus `GradientWarning` when `surface − air > 12 °C` | The gradient is what burns an animal; the absolute air temperature alone can look fine |

This asymmetry is deliberate and belongs in the report: not every metric deserves the same alert machinery.

## 5. Exposure index and daily summary maths

Daily summary is computed per terrarium per **local** day, using the phase in force at each minute.

### 5.1 Temperature exposure (degree-hours)

$$E^{+} = \sum_{i} \max\!\left(0,\; T_i - T^{target}_{max,phase(i)}\right)\cdot \Delta t_i
\qquad
E^{-} = \sum_{i} \max\!\left(0,\; T^{target}_{min,phase(i)} - T_i\right)\cdot \Delta t_i$$

with $\Delta t_i = 1/60$ h and $T_i$ linearly interpolated between samples at 1-minute resolution.

Worked example: target 26–32 °C; 40 minutes at an interpolated 34.0 °C and 20 minutes at 33.0 °C
(no cold excursion):

$$E^{+} = (34.0-32.0)\cdot\tfrac{40}{60} + (33.0-32.0)\cdot\tfrac{20}{60}
= 2.0\cdot 0.667 + 1.0\cdot 0.333 = 1.67\ \text{°C·h}$$

Note what this buys over `outOfRangeMinutes = 60`: two days can both have 60 out-of-range minutes while one
was 1.67 °C·h and the other 6.0 °C·h. The second is a genuinely worse day for an ectotherm, and the report
can say so with a number rather than an adjective.

### 5.2 Humidity exposure (%-hours)

$$E^{RH} = \sum_i \max\!\left(0,\; RH^{target}_{min} - RH_i\right)\cdot \Delta t_i
\quad\text{and}\quad
\sum_i \max\!\left(0,\; RH_i - RH^{target}_{max}\right)\cdot \Delta t_i$$

Dry excursions are weighted equally to wet ones here, but reported **separately** (`HumidityDryHours` /
`HumidityWetHours`) so a reader can see the direction — for a tropical species, dryness is the dangerous
direction; for a semi-arid species, the reverse.

### 5.3 Light hours and deficit

$$H_{light} = \sum_i \left[\, L_i \ge L_{threshold} \,\right]\cdot \Delta t_i,
\qquad
D = \max\!\left(0,\; H_{required} - H_{light}\right)$$

### 5.4 Compliance percentage

$$\text{compliance} = 100\cdot\left(1 - \frac{\text{outOfRangeMinutes}}{\text{minutesWithData}}\right)$$

Alone this is misleading (see `02-design/04` §6: a day with 40% data can show 100% compliance), so it is
**always** rendered next to `coveragePct` and `IsLowConfidence`.

### 5.5 Coverage

$$\text{coverage} = 100\cdot\frac{\text{samplesReceived}}{\text{expectedSamples}},
\qquad \text{expectedSamples} = \frac{\text{secondsWithTerrariumBoundDevice}}{\text{samplingInterval}}$$

`IsLowConfidence = coverage < 80`. Aggregation rules: `avg` is time-weighted (not a plain mean of samples)
so a gap cannot skew it; `min`/`max` are sample-based; the summary records `sampleCount` so a reader can
audit the number.

## 6. Summary computation pseudocode

```csharp
async Task<DailyEnvironmentalSummary> BuildAsync(Guid terrariumId, DateOnly localDate, CancellationToken ct)
{
    var tz = await ResolveTimezoneAsync(terrariumId, ct);
    var (fromUtc, toUtc) = LocalDayToUtcRange(localDate, tz);
    var phases = await phaseResolver.GetPhaseTimelineAsync(terrariumId, fromUtc, toUtc, ct);   // from photoperiod
    var thresholds = await snapshotStore.GetEffectiveForRangeAsync(terrariumId, fromUtc, toUtc, ct);

    var minutes = await minuteSeries.QueryAsync(terrariumId, fromUtc, toUtc, ct);   // interpolated, 1-min grid, null for gaps

    var perMetric = minutes.GroupBy(m => m.MetricId).ToDictionary(g => g.Key, g => new Accumulator(g));
    foreach (var acc in perMetric.Values) acc.Apply(thresholds, phases, recoveryMargin: true);

    return new DailyEnvironmentalSummary
    {
        TerrariumId = terrariumId,
        LocalDate = localDate,
        CoveragePct = ComputeCoverage(minutes),
        TempExposureDegCHours = perMetric[TempC].HotExposureHours + perMetric[TempC].ColdExposureHours,
        HumidityDryHours = perMetric[Humidity].DryHours,
        HumidityWetHours = perMetric[Humidity].WetHours,
        LightHours = perMetric[Light].HoursAboveThreshold,
        LightDeficitHours = perMetric[Light].DeficitHours,
        OutOfRangeMinutesJson = JsonSerializer.Serialize(perMetric.ToDictionary(k => k.Key, v => v.Value.OutOfRangeMinutes)),
        AlertCount = await alerts.CountAsync(terrariumId, fromUtc, toUtc, ct),
        CriticalAlertCount = await alerts.CountCriticalAsync(terrariumId, fromUtc, toUtc, ct),
        IsLowConfidence = ComputeCoverage(minutes) < 80,
        ComputedAt = DateTimeOffset.UtcNow,
    };
}
```

Upsert key `(TerrariumId, LocalDate)` makes recomputation idempotent, which is what lets a late back-fill
correct yesterday's summary without special cases (BR-15.3/BR-14.1).

## 7. Species profile design (how the seeded profiles are shaped)

Each seeded profile is a small, reviewable object — not a spreadsheet dump:

| Field | Tropical | SemiArid (demo) | Arid |
|---|---|---|---|
| Example keeper species | Crested gecko / mourning gecko | **Leopard gecko** | Bearded dragon |
| Temperature day target | 24–28 °C | 26–32 °C | 30–38 °C (basking to 40) |
| Temperature night target | 20–24 °C | 22–27 °C | 24–28 °C |
| Temperature critical | 18–31 °C | 22–34.5 °C | 20–42 °C |
| Humidity target | 60–80 %RH | 30–40 %RH (70–80 % in the humid hide) | 30–40 %RH |
| Humidity critical | 40–95 %RH | 20–60 %RH | 20–60 %RH |
| Light threshold | 500 lx, ≥ 10 h | 1 000 lx, ≥ 8 h | 2 000 lx, ≥ 10 h |
| UVI target | 0–1.0 (optional, low) | 0–1.5 | 1.0–3.5 (with UVB lamp) |
| Photoperiod | 12 h from 07:00 | 12 h from 07:00 | 12 h from 08:00 |

`[TBC-1]` **These are typical published husbandry ranges, not laboratory-derived limits.** They are seeded
as a starting draft; §5 of `07-appendices/05` is the verification checklist that must be closed, with the
citation per row, before the demo. Any row without a citation does not ship — that is a gate, not a
preference, because the brief's requirement #4 is precisely that thresholds come from literature.

## 8. Things deliberately *not* modelled in v1

| Not modelled | Consequence | Where it goes next |
|---|---|---|
| Individual animal identity / health state | The system reasons about the habitat, not the animal | v2 (behaviour input) |
| Species-specific seasonal cycles (brumation, breeding season) | A single profile year-round; the keeper adjusts bands manually per season | v1.1 (seasonal profiles) |
| Sex/age differences in requirements | Ignored; notes field only | Not planned |
| Humidity hide vs ambient humidity | One humidity number; the profile notes the distinction in `Notes` | v1.1 (multi-sensor per terrarium) |
| Microclimate gradient beyond basking surface | Only air + one surface probe | v1.2 |
| Feed/water events | Out of scope entirely | v2 (interaction log alongside behaviour) |
