# 05 — Species Threshold Reference and Literature Basis

This appendix supplies the numbers the alert engine uses. It is the document that satisfies the brief's
requirement §6 ("define suitable environmental thresholds based on papers, research and specialist books for
the chosen species").

---

## 0. Read this first — what these numbers are and are not

- The tables below are **typical published captive-husbandry ranges** for each species/climate zone, assembled
  from the sources in §6. They are the *starting draft* the software ships with.
- They are **not** laboratory-derived physiological limits, and SmartReptile does **not** claim metrological
  accuracy (limitation L-04). They are defensible target envelopes for a hobby enclosure.
- **Gate:** every row must have a source reference and be checked by a team member before the demo
  (§5 checklist). The database enforces that a built-in `Threshold` has a non-empty `SourceRef`; the checklist
  enforces that the reference is real and supports the number.
- Where a metric is left `NotMeasured` (e.g. UV without the LTR390), no alert of that kind exists — the UI
  says so instead of inventing a value.

**Symbols:** `T` temperature (°C) · `RH` relative humidity (%RH) · `lx` illuminance · `UVI` UV index ·
`Crit` = critical band (outside ⇒ Critical) · `Target` = target band (outside ⇒ Warning) · dwell and recovery
margin are engine parameters (FR-11).

---

## 1. Profile: Tropical (humid forest) — e.g. crested gecko (*Correlophus ciliatus*), mourning gecko (*Lepidodactylus lugubris*)

| Metric | Phase | Target | Critical | Dwell warn/crit | Recovery margin | Rationale |
|---|---|---|---|---|---|---|
| Temperature | Day | 24.0 – 28.0 | 18.0 – 31.0 | 5 / 2 min | 0.5 °C | These species are cool-adapted for geckos; sustained > 30 °C is a real risk, and heat, not cold, is the common killer indoors |
| Temperature | Night | 20.0 – 24.0 | 16.0 – 27.0 | 10 / 3 min | 0.5 °C | A natural night drop is expected; a *warm* night is the anomaly |
| Humidity | Any | 60 – 80 | 40 – 95 | 15 / 5 min | 3 %RH | High humidity is the normal state; a dry spell causes shedding problems |
| Light (lx) | Day | ≥ 500 lx for ≥ 10 h/day | — (accumulated rule) | 15 min (LightDeficit at 21:00) | — | These geckos are crepuscular; light matters as a cycle signal more than as intensity |
| UV index | Day | 0.0 – 1.0 | 0.0 – 2.0 | 15 / 5 min | 0.1 | Low-level UVB is optional-to-beneficial; **only if the LTR390 is fitted** |

## 2. Profile: Semi-arid / semi-desert — **demo species**: leopard gecko (*Eublepharis macularius*)

Chosen as the demo species because its husbandry envelope is among the best documented in the hobby literature
and it is a common starter animal, so the thresholds can be checked against several independent sources.

| Metric | Phase | Target | Critical | Dwell warn/crit | Recovery margin | Rationale |
|---|---|---|---|---|---|---|
| Temperature | Day | 26.0 – 32.0 | 22.0 – 34.5 | 5 / 2 min | 0.5 °C | Thermal-gradient species: air temperature near the warm side in the low 30s, cool side mid-20s. **34.5 °C is where an unmonitored heat mat becomes dangerous** |
| Temperature | Night | 22.0 – 27.0 | 18.0 – 31.0 | 10 / 3 min | 0.5 °C | A 3–5 °C night drop is physiologically beneficial, not a fault |
| Temperature (surface, optional) | Day | 30.0 – 34.0 | 22.0 – 38.0 | 5 / 2 min | 1.0 °C | Basking surface; `GradientWarning` also fires if surface − air > 12 °C |
| Humidity | Any | 30 – 40 | 20 – 60 | 15 / 5 min | 3 %RH | Ambient (arid) conditions; the *humid hide* is 70–80 %RH and is not what this sensor measures — the profile `Notes` says so explicitly |
| Light (lx) | Day | ≥ 1 000 lx for ≥ 8 h/day | — (accumulated rule) | 15 min | — | Provides a clear day/night cycle; strong UVB is not required for this species |
| UV index | Day | 0.0 – 1.5 | 0.0 – 2.5 | 15 / 5 min | 0.1 | Low-level UVB, optional; only with the LTR390 fitted |

**Known limitation to state in the report:** the profile's humidity band (30–40 %RH) describes *air*
humidity. Leopard geckos need access to a humid hide at 70–80 %RH during shedding. A single humidity sensor
cannot represent both, which is why the profile carries the note and why multi-sensor support is on the v1.1
list (`03-implementation/06` §8).

## 3. Profile: Arid / desert — e.g. bearded dragon (*Pogona vitticeps*)

| Metric | Phase | Target | Critical | Dwell warn/crit | Recovery margin | Rationale |
|---|---|---|---|---|---|---|
| Temperature | Day (basking) | 38.0 – 42.0 | 30.0 – 45.0 | 5 / 2 min | 1.0 °C | A true heliotherm needs a hot spot; the air temperature alone understates its needs |
| Temperature | Day (ambient/cool side) | 28.0 – 33.0 | 24.0 – 36.0 | 5 / 2 min | 1.0 °C | Provided as a second profile variant (`Arid-cool`) so both zones can be monitored. **Seeded 2026-10-07** as `Arid-cool (bearded dragon, ambient)`, which ships this band and the humidity band and nothing else — no surface or UV band, because both belong to the basking zone the variant does not describe |
| Temperature | Night | 24.0 – 28.0 | 18.0 – 32.0 | 10 / 3 min | 1.0 °C | Desert nights are cool; a warm night is not automatically harmful but is not the target |
| Temperature (surface, optional) | Day | 38.0 – 45.0 | 28.0 – 50.0 | 5 / 2 min | 1.0 °C | The probe sits **on the basking rock, not in the air**: a heliotherm's surface runs well above the air above it, which is why this band is warmer than the air band above. `GradientWarning` also fires if surface − air > 12 °C |
| Humidity | Any | 30 – 40 | 20 – 55 | 15 / 5 min | 3 %RH | Low ambient humidity; persistently wet conditions cause respiratory problems |
| Light (lx) | Day | ≥ 2 000 lx for ≥ 10 h/day | — (accumulated rule) | 15 min | — | High light intensity + a strong UVB gradient is the accepted standard for this species |
| UV index | Day | 1.0 – 3.5 | 0.0 – 5.0 | 15 / 5 min | 0.2 | **Requires a proper UVB lamp**; without a UVB source the lower bound cannot be met, and the app reports "below target" rather than pretending it is fine (UV-Tool guidance, §6 source 1) |

## 4. Climate-zone plausibility ranges (used for the non-blocking sanity warning, BR-10.5)

| Climate zone | Air temperature ceiling (`TempC`) | Target humidity ceiling | Typical photoperiod | Light threshold |
|---|---|---|---|---|
| Tropical | 28–30 °C | 70–85 %RH | 12 h | 500 lx |
| SemiArid | 31–33 °C | 40–50 %RH | 12 h | 1 000 lx |
| Arid | 40–44 °C (basking) | 40 %RH | 12–14 h | 2 000 lx |
| Temperate | 26–30 °C | 50–70 %RH | 12–14 h (seasonal) | 800 lx |

If a user enters a band contradicting the zone's plausible range, the UI shows a non-blocking warning
("a desert profile normally requires …") — helpful, never paternalistic.

**The ceiling is per metric, and `SurfaceTempC` is judged on its own scale** (clarified 2026-09-29). The
temperatures above are **air** ceilings, so a `TempC` band is compared against them. A basking *surface* is
legitimately hotter than the air above it — that is the entire point of the surface band in §2 and §3 — so
measuring a surface band against an air ceiling would flag the very shape the seeded profiles ship. A
`SurfaceTempC` band is therefore compared against the ceiling implied by that profile's own seeded surface band
(its `CriticalMax`): **38 °C** for SemiArid, **50 °C** for Arid. Without the split, the Arid surface band's 45 °C
target max would trip its own sanity warning — exactly the false positive a *non-blocking* notice must never
produce.

**The mirrored split applies to a profile that monitors the ambient side** (added 2026-10-07, when the
`Arid-cool` variant was seeded). The Arid row's 40–44 °C is a **basking** maximum, so the variant's documented
28–33 °C day band would trip the very warning the profile ships with. A profile that declares itself an
ambient/cool-side variant is therefore judged against the ambient envelope — **28–33 °C**, the range §3 gives that
variant — and the `SurfaceTempC` rule is skipped for it, because it ships no surface band to judge. This case was
found the same way the surface one was: by seeding the band and running the validation test, not by reading the
table. What the rule needs from a client is a way to know which zone a profile monitors; the prototype holds that
as a per-profile envelope override, and `03-implementation/06` §2 records the API half as still open.

## 5. Verification checklist — **gate before the demo**

Each row: locate the number in a source, record the exact page/table, and have a second team member confirm it.
A row without a page reference is not verified, even if the number "looks right".

| # | Profile | Metric | Value to verify | Source (§6 ref #) | Page/table | Verified by | Date | Status |
|---|---|---|---|---|---|---|---|---|
| 1 | SemiArid | Day target 26–32 °C | | 2, 4, 6 | | | | ☐ |
| 2 | SemiArid | Critical max 34.5 °C | | 4, 6 | | | | ☐ |
| 3 | SemiArid | Night target 22–27 °C | | 2, 6 | | | | ☐ |
| 4 | SemiArid | Humidity 30–40 %RH | | 4, 6 | | | | ☐ |
| 5 | SemiArid | Humid-hide 70–80 %RH (note text) | | 6 | | | | ☐ |
| 6 | Arid | Basking 38–42 °C | | 2, 4 | | | | ☐ |
| 7 | Arid | Ambient 28–33 °C | | 4 | | | | ☐ |
| 8 | Arid | UVI 1.0–3.5 with UVB | | 1 | | | | ☐ |
| 9 | Tropical | Day 24–28 °C | | 2, 5 | | | | ☐ |
| 10 | Tropical | Humidity 60–80 %RH | | 2, 5 | | | | ☐ |
| 11 | All | Photoperiod 12 h | | 2, 4 | | | | ☐ |
| 12 | All | Light threshold values (500/1 000/2 000 lx) | | 2, 7 | | | | ☐ note these are husbandry practice, not physiology — flag as such |
| 13 | All | Dwell/recovery parameters are **team design choices**, not literature values | — | — | | | | ☐ state this explicitly in the report |
| 14 | Arid | Basking **surface** 38–45 °C | | 4, 7 | | | | ☐ |

Row 13 matters: dwell time and hysteresis come from engineering judgement about sensor noise and alert
fatigue. The report must not imply they are sourced from biology.

**Drafted wording for rows 12 and 13** (2026-10-07 — both are statements rather than numbers, so neither needs a
source; paste them into the report once a second member signs the row).

- *Row 12 — the illuminance targets.* "The illuminance values (500 / 1 000 / 2 000 lx) are husbandry practice
  rather than physiology: they are the figures the cited husbandry references use to describe a usable day/night
  cycle, not measured thresholds below which an animal is harmed. SmartReptile therefore applies them as an
  accumulated light-hours rule with a 15-minute dwell, and reports a deficit rather than a severity."
- *Row 13 — dwell and recovery.* "The dwell times, recovery margins and hysteresis in §1–§3 are engineering
  choices about sensor noise and alert fatigue, not literature values. `Threshold.SourceRef` says so in the
  database itself. No source is offered for them, and the report does not imply one."

Row 14 was added on 2026-09-29 and is the one row that exists because of a *documentation* gap rather than a
number: the database has always shipped this band, but neither §3 nor this checklist mentioned it (item 2 below).

**Two things a reader of this checklist has to know** (added 2026-09-23, when the seeder side was wired up;
item 2 updated 2026-09-29):

1. **It is not a one-to-one map of the seeded bands.** Rows 5, 11, 12 and 13 describe profile-level data or engine
   parameters — the humid-hide note, the photoperiod, the lx values, dwell/recovery — rather than bands. Closing a
   row is therefore not a documentation-only act: `ReferenceDataSeeder` has to agree with it. While a row is
   unsigned the band's `SourceRef` reads `PENDING VERIFICATION` and its `SourceUrl` is **null** rather than a link
   to a source nobody has opened. When a row is signed, pass the verified citation at that band's call site and
   decrement `ReferenceDataSeeder.BandsAwaitingVerification` in the same commit;
   `SchemaAndSeedingTests.Bands_awaiting_verification_match_the_declared_count` fails if the two drift, so the
   stamp cannot outlive this checklist.
2. **Coverage against the seeded bands** (found by comparing §3 with the seeder rather than reading either one
   alone). Both gaps found on 2026-09-23 are now closed:
   - **Closed 2026-09-29 — was gap (b).** The seeded `Arid` **surface** band (target 38.0–45.0 °C, critical
     28.0–50.0 °C, margin 1.0 °C) was in neither §3 nor this checklist, so the database shipped a band that no
     document described. It is now the §3 row and row 14 above, so §3, §5 and the seeder described the same 17
     bands as of that date. No count changed: this band was already one of the 14 pending ones.
   - **Closed 2026-10-07 — was gap (a).** Row 7 verifies an *Arid ambient 28–33 °C* band belonging to an
     `Arid-cool` variant that §3 described and the seeder did not create. The decision taken on 2026-10-07 was to
     **seed the variant** rather than drop the row: `ReferenceDataSeeder` now ships `Arid-cool (bearded dragon,
     ambient)` with that band plus the humidity band, and with no surface or UV band, because both belong to the
     basking zone the variant does not describe. This is the decision that moves band counts, so the counts were
     updated wherever they were quoted — `README`, `03-implementation/07` (task 1.8), `03-implementation/08` and
     `05-release/01` — and `SchemaAndSeedingTests` now pins four profile names, 19 bands and the variant's shape.
     §3, §5 and the database describe the same 19 bands over four profiles.
3. **Row 14 was appended, not inserted, and rows 1–13 keep their numbers on purpose.** The seeder writes row
   numbers into the data: `Threshold.SourceRef` literally reads `…docs/07-appendices/05 §5 row 13` for the dwell
   statement and `…§5 row 12` for the light bands. Renumbering would leave every already-seeded row — including
   rows already written into a deployed database — citing the wrong row, so the new row sorts after the `All`
   rows; read the **Profile** column, not the position.

**Bibliographic pre-pass — 2026-10-07 (no row signed by it).** A metadata pass was run against the open
bibliographic indexes (OpenAlex, Crossref) to shorten the sitting this checklist needs. It resolved what an index
can resolve and **recorded no page number it had not read** (§6's rule: a fabricated page is worse than a missing
one). The `Page/table` column is still empty for every row on purpose.

| Source | What the pre-pass resolved | What a person must still do |
|---|---|---|
| 1 — UV-Tool | The title is *How much **UVB** does my reptile need?* (**not** "UV-B"); 2016; DOI `10.19227/jzar.v4i1.150`, which places it in *JZAR* 4(1); open access at `jzar.org/index/article/view/150`; ISSN 2214-7594 | Locate the page/table that states the Arid UVI band (row 8). `jzar.org` did not resolve from the build machine and Crossref holds no record for this DOI, so its page range is still unknown |
| 2 — Vitt & Caldwell | The 2nd edition (2001, Academic Press) is **Zug**, Vitt & Caldwell; later editions are Vitt & Caldwell alone | Name the edition used. §6 below cited no edition, and the two differ in authorship |
| 3 — Seebacher & Franklin | **Fully resolved:** *Journal of Comparative Physiology B* **175**(8): 533–541, 2005, DOI `10.1007/s00360-005-0007-1` | Locate a preferred-body-temperature figure inside it. No checklist row cites source 3 today, so this is a §2 rationale upgrade rather than a row |
| 4 — Mader & Divers | Not in the open indexes. Their nearest match was a *different* Elsevier title, *Current Therapy in Reptile Medicine and Surgery* (2014, DOI `10.1016/c2010-0-67117-3`) — it is not the cited work and must not be substituted for it | Edition, year and page from the physical copy |
| 5–8 — husbandry books | Not indexed in OpenAlex or Crossref (small-press husbandry books) | Edition, year and page from the copies. This is the majority of the remaining work: rows 1, 5, 6, 7, 9, 10, 11 and 14 lean on 4, 6 and 7 |
| 9–10 — AZA / RSPCA / BIAZA | Not reachable from the build machine: `aza.org` returned `403`, the guessed `rspca.org.uk` care URLs `404` and its site search is client-rendered, and the search engines that could have located the sheets (DuckDuckGo, Bing, Ecosia, Mojeek) were blocked or returned no usable results | Produce the care sheets, or replace them with guidance that can be produced. A source nobody can open should be dropped from §6 rather than cited |

What the pass does **not** change: `ReferenceDataSeeder.BandsAwaitingVerification` stays **14**, because no range
has been verified by a person yet. What it also leaves behind is a warning for §6 — the hygiene note there asks for
a "page used" column that only §5 carries, and §5's `Page/table` is therefore where those references belong.

## 6. Candidate literature and reference list

> **Citation hygiene.** Titles/journals below are real works in the field, but **edition, year, page and DOI
> details must be verified against the physical/online copy before submission** — a fabricated page number is
> worse than a missing one. Fill in the "page used" column during verification. Sources marked *(institutional)*
> are care-guidance documents rather than peer-reviewed papers; they are acceptable for husbandry ranges, and
> the report should label them as such.

| # | Reference | Used for | Type |
|---|---|---|---|
| 1 | Baines, F. M., Chattell, J., Dale, J., Garrick, D., Gill, I., Goetz, M., Skelton, T., & Swatman, M. — *How much UVB does my reptile need? The UV-Tool, a guide to the selection of UV lighting for reptiles and amphibians in captivity*, Journal of Zoo and Aquarium Research, 4(1), 2016. DOI `10.19227/jzar.v4i1.150`; open access: `jzar.org/index/article/view/150` | UV index targets, UVB reasoning, UVI vs µW/cm² distinction | Peer-reviewed |
| 2 | Vitt, L. J., & Caldwell, J. P. — *Herpetology: An Introductory Biology of Amphibians and Reptiles*, Academic Press — **name the edition used:** the 2nd edition (2001) is Zug, Vitt & Caldwell, later editions are Vitt & Caldwell alone | Ectothermy, thermoregulation behaviour, why duration matters | Textbook |
| 3 | Seebacher, F., & Franklin, C. E. — *Physiological mechanisms of thermoregulation in reptiles: a review*, Journal of Comparative Physiology B **175**(8): 533–541, 2005. DOI `10.1007/s00360-005-0007-1` | Preferred body temperature ranges, thermal performance curves | Review |
| 4 | Mader, D. R., & Divers, S. J. (Eds.) — *Reptile Medicine and Surgery*, Saunders/Elsevier — edition and year still to be pinned; it is **not** in OpenAlex or Crossref (their nearest match was a different title, *Current Therapy in Reptile Medicine and Surgery*, 2014) | Clinical consequences of temperature/humidity extremes; husbandry-related disease | Textbook |
| 5 | De Vosjoli, P., Fast, F., & Repashy, A. — *Rhacodactylus: The Complete Guide to their Selection and Care* | Tropical (crested/mourning gecko) bands | Husbandry book |
| 6 | De Vosjoli, P., Tremper, R., & Klingenberg, R. — *The Leopard Gecko Manual*, Advanced Vivarium Systems | Leopard gecko bands, humid-hide requirement, night drop | Husbandry book |
| 7 | De Vosjoli, P., et al. — *The Bearded Dragon Manual*, Advanced Vivarium Systems | Arid species basking/ambient bands, UVB requirement | Husbandry book |
| 8 | Henkel, F.-W., & Schmidt, W. — *Geckos: Biology, Husbandry, Reproduction*, TFH Publications | Gecko microclimate, substrate temperature | Husbandry book |
| 9 | Association of Zoos and Aquariums (AZA) Reptile & Amphibian TAG care manuals *(institutional)* | Cross-check of captive ranges, institutional practice | Care guidance |
| 10 | RSPCA / BIAZA species care sheets *(institutional)* | Welfare-oriented sanity check on husbandry ranges | Care guidance |

**Search trail to document** (shows the literature work was real): databases used (Google Scholar, ScienceDirect,
ResearchGate), search strings (e.g. `"Eublepharis macularius" preferred body temperature`,
`terrarium humidity shedding gecko`), how many hits screened, and why the ones above were selected. This
belongs in report section R3.4/R13.

## 7. Sensors vs thresholds — the honest mapping

| Threshold metric | Sensor | What it actually measures | Caveat |
|---|---|---|---|
| `TempC` | SHT31 | Air temperature at one point | Not a gradient; the terrarium has a warm and a cool side |
| `HumidityPct` | SHT31 | Relative humidity at one point | Not the humid hide; drifts in a condensing enclosure |
| `LightLux` | BH1750 | Illuminance at the sensor plane | No UVB dose; arbitrary placement changes the number |
| `UvIndex` | LTR390 | Erythemally weighted UV index on a horizontal plane | Indicative; not a UVB dose in µW/cm² and not calibrated against a spectroradiometer |
| `SurfaceTempC` | DS18B20 | Surface/contact temperature where the probe sits | Placement-critical (see `07-appendices/04` §4) |

A band can only be as meaningful as the measurement behind it. This table is what the report's "limitations"
paragraph should point at.

## 8. Custom profiles (user-created)

Users can create their own profile for a species not covered (`POST /species-profiles`, or "duplicate" a
built-in and adjust). For custom profiles:
- the band-ordering and critical-enclosure rules still apply (DI-05);
- a `SourceRef` is **encouraged** (free text) but not enforced — the app nudges with a "where did this number
  come from?" hint, because a keeper's own experience is a legitimate source and pretending otherwise would be
  dishonest;
- the climate-zone sanity warning is skipped (a custom profile has no zone), so the UI instead shows the
  plausibility range for the metric as advisory text.
