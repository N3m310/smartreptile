# 02 — Contribution Table

This table is a **graded artefact** (rubric: team introduction + contribution). It is filled in continuously
during the project, not reconstructed at the end — reconstructed tables never match the git history and
reviewers can tell.

## 1. How to fill it in

Rules:
1. **One row per person per track**, not one row per person. Most members touch more than one area, and the
   table is more credible when it shows that.
2. **Quantify with artefacts:** commits, files, tests, pages, or hours. "Helped with backend" is not a
   contribution; "implemented `ThresholdDecision` + 9 unit tests (TC-U-10…17), 3 days" is.
3. **Percentages must sum to 100%** and match the effort split agreed at the final review.
4. **Every claim is verifiable:** the artefact column points at a commit range, a file, a test id, or a page
   range of the report.
5. **Amendments are appended**, not rewritten — if someone took over a task, both the original owner and the
   takeover are listed with dates.

## 2. Template (fill in)

### Member 1 — *[Full name, student id]*
| Track | Contribution | Artefacts | Effort |
|---|---|---|---|
| Firmware/hardware | | | |
| Docs | | | |
| Other | | | |
| **Subtotal** | | | **__%** |

### Member 2 — *[Full name, student id]*
| Track | Contribution | Artefacts | Effort |
|---|---|---|---|
| Backend | | | |
| QA | | | |
| **Subtotal** | | | **__%** |

### Member 3 — *[Full name, student id]*
| Track | Contribution | Artefacts | Effort |
|---|---|---|---|
| App/web | | | |
| Report | | | |
| **Subtotal** | | | **__%** |

**Total = 100%.**

## 3. Track-level allocation summary

| Workstream | Hours (est.) | Member 1 | Member 2 | Member 3 |
|---|---|---|---|---|
| Requirements + literature (thresholds) | 20 | | | |
| Hardware bench + wiring + accuracy checks | 25 | | | |
| Firmware (sampling, transport, provisioning, buffer) | 45 | | | |
| Backend (API, EF Core, ingest, engine, notifications) | 70 | | | |
| DB schema, migrations, seeding | 15 | | | |
| Flutter app (screens, state, tests) | 55 | | | |
| Web dashboard | 20 | | | |
| Testing (unit/integration/widget/E2E) | 40 | | | |
| Docs (this doc set) | 30 | | | |
| Report writing + figures + formatting | 35 | | | |
| Demo preparation + rehearsal | 15 | | | |
| **Total** | **370** | | | |

## 4. Artifact index (links/commits for verification)

| # | Artefact | Where | Owner |
|---|---|---|---|
| 1 | Monorepo scaffold + CI | commit range | |
| 2 | `InitialSchema` migration + seeders | commit range | |
| 3 | `IngestPipeline` + workers | commit range | |
| 4 | `ThresholdDecision` + engine tests | commit range + `TC-U-10…20` | |
| 5 | Notification dispatcher + policy matrix tests | commit range + `TC-U-37…45` | |
| 6 | Rollup + daily summary + exposure maths | commit range + `TC-U-46…50` | |
| 7 | Firmware sampler/filters/ring buffer | commit range + `TC-U-FW-01…12` | |
| 8 | Firmware provisioning + MQTT/TLS transport | commit range | |
| 9 | Flutter app screens + providers | commit range | |
| 10 | Widget tests | commit range + `TC-W-01…18` | |
| 11 | Web dashboard (wallboard + report pages) | commit range | |
| 12 | Hardware BOM + wiring + accuracy log | `07-appendices/04`, QA §2 | |
| 13 | Threshold literature verification | `07-appendices/05` §5 checklist | |
| 14 | Mechanical/3D enclosure parts (if any) | photos + files | |
| 15 | This doc set (34 files) | commit range | |
| 16 | Report PDF + evidence folder | `report/` | |
| 17 | Release APK/AAB + signature proof | `05-release/01` §4 | |
| 18 | Demo script + rehearsal recording | `05-release/01` §6 | |

## 5. Honesty statement

Signed by all members, part of the report appendix:

> We agree that the contributions recorded above are accurate. We did not claim work we did not do, and where
> one member took over another's task we recorded both names and the date of the handover. AI coding
> assistants were used as tools for code drafting and documentation structure; all design decisions,
> measurements and verification results in this report were produced and checked by the team.

| Member | Name | Signature | Date |
|---|---|---|---|
| 1 | | | |
| 2 | | | |
| 3 | | | |

## 6. Notes for the reviewer (why this table looks the way it does)

- The track split exists because SmartReptile has three distinct interfaces (hardware, API, clients). Cross-track
  pairing was used deliberately so no single person is a bus factor for the demo.
- The count of hours in §3 adds up to a realistic one-semester effort for three students; if the final numbers
  differ, the table is corrected rather than scaled — inflated estimates are the easiest thing for a reviewer to
  challenge.
- Where a contribution is a *verification* rather than a *construction*, it is still listed (the 24 h soak, the
  accuracy comparison, the security checklist). Verification is work, and this system's value depends on it.
