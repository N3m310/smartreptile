# 01 — Technical Report Outline

Structure of the report deliverable (`.pdf`), mapped to the PRM393 rubric and to this doc set. Section codes
`R1…R14` are referenced from `04-quality/04-requirements-traceability-matrix.md`, so the numbering must not
change once writing starts.

**Target length:** 40–60 pages excluding appendices · **Format:** A4, 12 pt, 1.5 line spacing, page numbers,
figures numbered `Figure R4.2`, tables `Table R3.4` · **Language:** Vietnamese (per the course) with English
technical terms kept in place, figures and code comments in English.

---

## Rubric → section map

| Rubric item | Where it is satisfied | Evidence required |
|---|---|---|
| Team introduction, roles | R1.2 | Contribution table (`06-report/02`) signed by all members |
| Case study / problem analysis | R2.1–R2.3 | Photos of the terrarium, persona table, competitor/product comparison |
| Business analysis (users, value, feasibility) | R2.4–R2.6 | Personas, USP statement, cost BOM, feasibility table |
| System design & architecture | R4 | Layered architecture diagram, component diagram, sequence diagram of one sample |
| Requirements list (FR/NFR) | R3 | FR-01…18, NFR-01…12 with acceptance criteria |
| UI flow | R3.6, R4.4, R6.6 | Navigation graph, wireframes, real screenshots |
| Database / API design | R4.3, R4.5 | ERD, table reference, endpoint table, MQTT topic table |
| Implementation (source + comments) | R6 | Key code excerpts with explanations; link to the source archive |
| State management | R6.4 | Provider graph + a code excerpt showing one provider |
| Release build proof | R10.2 | Signature output, release-mode About screenshot, APK/AAB sizes |
| Testing (≥ 1 unit + ≥ 1 widget test) | R7 | Test counts per suite, coverage figures, three representative test cases |
| Commented code | R6.8 | Statement of the convention + an annotated excerpt |
| Demo readiness | R10.4 | Demo runbook, rehearsal checklist, fallback plan |
| Conclusion & contribution | R12, R14.1 | Contribution table + lessons learned |

---

## R1 — Introduction
**R1.1 Project context.** One paragraph: the brief (SmartReptile, v1 without AI), the course, the team size,
the deliverable set.
**R1.2 Team and roles.** Members, roles per track (firmware/hardware, backend, app/web, docs/report), summary
of contributions (details in R14.1).
**R1.3 Document conventions.** Glossary pointer, requirement id scheme, `MUST/SHOULD/MAY` convention,
statement that all thresholds carry a literature reference.
**R1.4 Summary of results.** What was built, what was measured, what was deliberately not built (a short,
honest table — this is the first thing a reviewer reads and the fastest way to earn trust).

## R2 — Case study and business analysis
**R2.1 The problem.** Reptile keepers judge conditions by feel; discrete meters exist but nothing integrates
measure → store → display → alert; unsuitable conditions are the leading cause of stress/disease
(per the brief §2 and the cited husbandry literature).
**R2.2 The case: the model terrarium.** Physical setup (20×10 cm scale model), species chosen and why,
photos, the measured microclimate reality (e.g. the enclosure self-heating measured in QA §1.5).
**R2.3 Existing solutions compared.** Table: analog thermometer/hygrometer, digital combo meters, generic
IoT kits (Blynk/ThingSpeak-style), commercial terrarium controllers, DIY ESP32 projects. Columns: what it
measures, storage, alerting, species awareness, cost, gap.
*Positioning:* the only option in the comparison that treats **species-specific bands as data with sources**
and accumulates a reusable dataset.
**R2.4 Users and personas.** P1–P4 from `01-product/01` §4, with needs/pains.
**R2.5 Value and novelty.** The four commitments from `01-product/01` §8 (integration, data over intuition,
dataset for v2, dwell+exposure rather than peak readings).
**R2.6 Feasibility.** Cost (BOM ≤ 1.5 M VND), skills, timeline (6 milestones), technical risk summary (top 3
from `05-release/03` §1).
**R2.7 Scope boundary.** In/out of scope table, with the explicit statement that **AI is v2** and actuators
are out (ADR-008) — plus the reason: monitoring failure must never endanger the animal.

## R3 — Requirements
**R3.1 Requirement ids and conventions.**
**R3.2 Functional requirements.** FR-01…FR-18 with priority, rules and acceptance criteria (compressed from
`01-product/03`; the full text stays in the doc set).
**R3.3 Non-functional requirements.** NFR-01…NFR-12 with budgets and verification method.
**R3.4 Threshold provenance.** How the bands were derived, the citation table, and the statement of what is
*not* claimed (published husbandry ranges, not laboratory limits). Include the verification checklist status.
**R3.5 User stories and use cases.** US-01…US-20 (table) and UC-01…UC-07 (main + exception flows summarised;
one flow shown in full as an example — UC-04 alert lifecycle).
**R3.6 UI flow.** Navigation graph, screen inventory with purpose, and the state vocabulary table.

## R4 — System design and architecture
**R4.1 Architectural drivers.** The five drivers from `02-design/01` §1 with consequences.
**R4.2 Layered architecture.** L1 Sensing → L2 Edge → L3 Platform → L4 Experience; component diagram;
dependency rules; "who may change what" table.
**R4.3 Data model.** ERD, entity list with purpose, the three integrity rules that live in the database
(one open alert per key, one sample per `(device, seq)`, one device per terrarium) and *why* they are DB
constraints rather than code checks.
**R4.4 Interaction design.** Sequence diagram: sample → ingest → evaluate → notify → UI. Include the budget
table.
**R4.5 Interface design.** MQTT topics + payload contract; REST endpoint groups; error taxonomy
(RFC 7807 + codes); real-time events.
**R4.6 The evaluation engine.** The decision procedure, dwell/hysteresis/escalation, the four worked examples
from `03-implementation/06` §3, and the exposure-index formula with the hand-computed example.
**R4.7 Deployment view.** Compose topology, TLS, volumes, secrets handling.
**R4.8 Design decisions and rejected alternatives.** ADR summary table with the two most defensible entries
discussed in prose (server-side evaluation; normalised telemetry vs wide table).

## R5 — Hardware and firmware
**R5.1 Bill of materials.** Table with prices and the ≤ 1.5 M VND total.
**R5.2 Wiring and placement.** Pin map, I²C/1-Wire topology, the sensor placement rules (and why the surface
probe must not see the lamp directly).
**R5.3 Firmware architecture.** Task diagram, ring buffer, transport state machine, self-diagnostics.
**R5.4 Measurement accuracy.** Method, reference instruments, before/after table, the honest "indicative only"
statement.
**R5.5 Power and environment.** Current draw, enclosure notes, RSSI at the chosen location.
**R5.6 Firmware resource usage.** `pio run -t size` output, heap watermark at 24 h.

## R6 — Implementation
**R6.1 Repository and tooling.** Monorepo layout, pinned versions, CI jobs.
**R6.2 Backend.** Layer responsibilities, ingest pipeline, key code excerpt (e.g. `ThresholdDecision`), background
workers, query/performance decisions.
**R6.3 Data layer.** EF Core configuration highlights (filtered unique indexes, check constraints), migration
and seeding policy.
**R6.4 App state management.** Provider graph, `TelemetryProvider` excerpt, the three design decisions in
`03-implementation/05` §2 (derived freshness, one notify per batch, injectable clock), offline behaviour table.
**R6.5 Real-time path.** SignalR setup, reconnect + re-fetch rule, degraded polling mode.
**R6.6 UI/UX implementation.** Screenshot gallery (Home, Alerts detail, Thresholds, Claim, Report, Wallboard),
plus the "never render a value without its timestamp" rule and how the widget signature enforces it.
**R6.7 Web dashboard.** Structure and why no build step. The M1 evidence set is `06-report/snapshots/` — live API
responses plus captured pages, taken from the running stack; where the Live and Wallboard pages show their empty
state behind an `http_404` banner, that is the documented milestone boundary (the `/api/v1` routes are M2), not a
fault, and the caption must say so.
**R6.8 Code quality.** Commenting convention with an annotated excerpt, analyzer/format gates, coverage gates.

## R7 — Testing and quality assurance
**R7.1 Strategy and pyramid.** Test counts by layer, determinism rules (the "never `pumpAndSettle` with a
running ticker" class of lesson), what is *not* automated and why.
**R7.2 Results.** `dotnet test`, `flutter test`, `pio test -e native` summaries; coverage figures against the
70% gate.
**R7.3 The three most valuable tests,** shown in code: dwell (four minutes → nothing), idempotency
(duplicate `(device, seq)`), staleness rendering. Each with the failure it prevents.
**R7.4 E2E and soak results.** Coverage %, false positives, latencies, incident log.
**R7.5 Bug log.** From `05-release/03` §4, including the regression test added for each.
**R7.6 Traceability.** Reference to the matrix; state that no requirement ships without a test row.

## R8 — Security and privacy
**R8.1 Assets and threats.** Table from `02-design/06` §1.
**R8.2 Authentication and authorisation.** User auth, device auth, RBAC matrix, 404-vs-403 policy.
**R8.3 Provisioning flow.** Sequence diagram, claim-code properties, anti-enumeration behaviour, the
pairing-token decision (TBC-4 → `ADR-016`).
**R8.4 Transport and platform security.** TLS, headers, rate limits, secrets handling, dependency scanning.
**R8.5 OWASP IoT Top 10 review.** Table with **I3, I4 and I10 explicitly not fully addressed** and the reason —
a claim of ten green ticks would invite a question the team cannot answer.
**R8.6 Privacy.** Minimal data collection, camera off by default, deletion semantics (anonymise audit rows).
**R8.7 Verification.** The checklist results from `02-design/06` §9.

## R9 — Performance and reliability
**R9.1 Budgets and measurements.** k6 results vs NFR-01/02, with the scenario descriptions.
**R9.2 Database evidence.** Execution plans and logical reads for the four heaviest queries, with the
before/after of any optimisation.
**R9.3 Reliability drills.** Broker/DB/API restarts, node unplug, Wi-Fi restart, NTP blocked, disk pressure —
observed vs expected.
**R9.4 Soak.** 24 h coverage, alert precision, heap stability, storage measured and extrapolated to 90 days.
**R9.5 Observability.** Metrics list, the "alerts flat while out-of-range rises" heuristic, log rules.
**R9.6 Honest limitations of the measurements.** From `05-release/02` §8 (single host, 7-day base for storage,
dwell-inclusive latency).

## R10 — Deployment and release
**R10.1 Environment and runbook.** Compose topology, cold-start steps with timings.
**R10.2 Release artefacts and proof.** Firmware version + tag, API image tag, signed APK/AAB with signature
output and the release-mode screenshot, per-ABI sizes.
**R10.3 Reproducibility.** Clean-clone build result, pinned versions, archive verification.
**R10.4 Demo plan.** 15-minute script (R10.4.1), runbook, rehearsal evidence, fallback plan (recorded video,
bench mode), and the induced-excursion timing maths.
**R10.5 Submission package.** Zip contents, exclusions verification, PDF checklist.

## R11 — Results, limitations and future work
**R11.1 What works.** Demonstrated capabilities with screenshots.
**R11.2 Measured outcomes vs success metrics.** From `01-product/01` §7 (continuity, alert latency, false
positives, freshness, durability, threshold provenance, rubric readiness).
**R11.3 Limitations.** L-01…L-08, each with the honest reason.
**R11.4 v2 roadmap (AI).** Dataset readiness (feature schema, labels from `FalsePositive`/`SensorFault`
resolutions), candidate models, the ethics of camera data, and **why no AI is in v1**.
**R11.5 What we would do differently.** At least three concrete items (e.g. start the threshold literature
work in week 1; build the bench-mode fake sensors before milestone 2; automate Telegram earlier).

## R12 — Conclusion
Three paragraphs: the problem restated, what was built and evidenced, and the honest statement of where the
system's usefulness ends (one animal, one terrarium, indicative sensors, no actuation).

## R13 — References
- Husbandry / physiology sources, one per threshold row in the appendix (author, year, title, publisher, plus
  the page/table used). Format: IEEE or APA, applied consistently.
- Technical standards and references: MQTT 3.1.1/5.0 (OASIS), RFC 7807, OWASP IoT Top 10, SHT31/BH1750/LTR390/
  DS18B20 datasheets, ESP32 technical reference, EF Core and Flutter official docs.
- Course materials: PRM393 slides/labs used for the report structure.
- Statement of which sources were consulted but not used, if any (shows the search was real).

## R14 — Appendices
| # | Content | Source |
|---|---|---|
| R14.1 | Contribution table | `06-report/02` |
| R14.2 | ADR log (ADR-001…016) | `07-appendices/01` |
| R14.3 | SQL schema reference | `07-appendices/02` |
| R14.4 | MQTT + REST API specification | `07-appendices/03` |
| R14.5 | Hardware BOM, pin map, bring-up sequence + QA log | `07-appendices/04`, `04-quality/03` |
| R14.6 | Species threshold reference + literature list | `07-appendices/05` |
| R14.7 | v2 AI roadmap | `07-appendices/06` |
| R14.8 | Screenshots and evidence index | `06-report/snapshots/` (the M1 set) and `report/evidence/` (app + demo evidence added at M4/M6) |

---

## Writing order and owners

| Order | Sections | Owner | Depends on |
|---|---|---|---|
| 1 | R1, R2 | docs owner | persona + case study photos |
| 2 | R3 | docs owner | frozen requirements |
| 3 | R4 | architecture owner | ADR log |
| 4 | R5 | firmware owner | BOM, accuracy table, size output |
| 5 | R6 | implementation owners (one subsection each) | code freeze for excerpts |
| 6 | R7, R8, R9 | QA owner | test/bench/soak evidence |
| 7 | R10 | release owner | signed artefacts |
| 8 | R11, R12 | docs owner + all | everything above |
| 9 | R13, R14 | docs owner | citation checklist |

## Quality gates before submission

1. Every figure referenced in the text exists and is numbered; every table is referenced.
2. Every claim with a number cites the source of that number (a test id, a plan screenshot, a log, or a
   measurement).
3. Every limitation in `05-release/03` §7 appears in R11.3 — no silently dropped caveats.
4. The traceability matrix has no row with an empty Test cell.
5. Vietnamese prose is consistent; English technical terms are used where the industry uses them (no
   invented translations of "dwell time").
6. Table of contents, figure list, table list, and page numbers generated automatically.
