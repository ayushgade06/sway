# Sway — Implementation Plan

Each task is one independently testable unit. "Done" means its acceptance check passes. Complexity: S ≤ ½ day, M ≈ 1–2 days, L ≈ 3–5 days (one experienced C#/Win32 engineer).

---

## P1 — Hardware discovery · **mostly done in research**

**Objective:** know exactly what touchpad, driver and interfaces are present.

| # | Task | Status | Acceptance |
|---|---|---|---|
| 1.1 | Identify device, hardware IDs, driver | **Done**: PIXA3848, I²C-HID, inbox `input.inf` 10.0.26100.9818 | — |
| 1.2 | Enumerate raw input HID devices; find 0x0D/0x05 | **Done**: COL02 | — |
| 1.3 | Dump value caps (logical/physical/units) | **Done**: 123.0×74.8 mm, 5 contacts, parallel layout | — |
| 1.4 | Test direct HID open | **Done**: read access → error 32 | — |
| 1.5 | Turn the probe into `tools/Sway.Probe` (`probe caps`, `probe report`) so **community members** can run it on their hardware | Todo, **S** | Prints a report like TECHNICAL_RESEARCH §0 on any PTP laptop; `probe report` emits paste-ready Markdown with no personal data (no username, serials, or device instance paths); exits non-zero with "no PTP collection" otherwise |
| 1.6 | Early community call: publish Probe as a pre-release and collect hardware reports before P3 tuning | S | ≥ 5 reports from different touchpad vendors, including at least one hybrid-mode device |

**Risk:** none remaining for this device. **Files:** `tools/Sway.Probe/Program.cs`, `Native.cs`.

---

## P2 — Input proof of concept · **the gate**

**Objective:** prove live one-finger physical coordinates in the background with the touchpad untouched (E1, E2, E4, E6), and test cursor freeze (E3).

| # | Task | Cx | Depends | Acceptance |
|---|---|---|---|---|
| 2.1 | Message-only window on a dedicated thread; register 0x0D/0x05 + INPUTSINK + DEVNOTIFY | S | 1.5 | WM_INPUT count increases while touching; **cursor/taps/scroll behave normally** (manual) |
| 2.2 | `PtpReportParser`: caps → per-contact (id, tip, conf, X, Y) in logical and mm | M | 2.1 | Unit test: hand-built 30-byte report → expected contacts. Live: a full horizontal swipe spans ≈0→123 mm |
| 2.3 | Hybrid-mode assembly | S | 2.2 | Unit test with synthetic split reports (contact count only in first) |
| 2.4 | `probe live` view: live contact table + ASCII pad map | S | 2.2 | Visually correct finger position (E1) |
| 2.5 | `probe record file.json` / replay | S | 2.2 | Recording of 10 scripted gestures saved to `tests/data/` (see list below) |
| 2.6 | Background test (E2) and elevated test (E4) | S | 2.4 | Frames arrive with Notepad, browser, full-screen video focused; E4 result written into FEASIBILITY_REPORT |
| 2.7 | E3 `probe freeze`: LL hook logs touchpad moves; hold Ctrl to drop them | S | 2.1 | Pass/fail recorded. If pass: cursor still while Ctrl+swipe, normal after |
| 2.8 | E6: frame rate from scan time | S | 2.2 | Hz and jitter recorded |

**Scripted recordings for 2.5:** right-edge up / down / up-then-down; left-edge up / down; centre vertical mousing; centre stroke drifting into the right edge; tap in edge zone; two-finger scroll starting at the edge; palm resting on the edge while the other hand mouses.

**Gate:** 2.1, 2.2, 2.4 and 2.6 must pass, or stop and re-plan (driver route, DECISIONS D-03).
**Risks:** null `hDevice` (handled in 2.1), hybrid mode on other hardware (2.3).

---

## P3 — Gesture recognition

**Objective:** a deterministic recognizer that triggers on edge swipes and never on ordinary mousing.

| # | Task | Cx | Depends | Acceptance |
|---|---|---|---|---|
| 3.1 | `Settings` record + JSON load/save with defaults and clamping | S | — | Round-trip test; corrupt file → defaults |
| 3.2 | `EdgeGestureRecognizer` states Idle/Candidate/Active/Rejected/Ended | M | 2.2 | Unit tests per transition (synthetic frames) |
| 3.3 | Activation: start-in-zone, ≥4 mm, vertical dominance 2:1 | S | 3.2 | Tests: tap → no event; horizontal stroke from zone → Rejected; 4 mm vertical → Started |
| 3.4 | Proportional mapping + 0.3 mm jitter filter + optional curve | S | 3.2 | 30 mm up at 60 mm-per-range → Δ ≈ +0.5; reversal yields negative deltas |
| 3.5 | Multi-touch and palm rejection (second contact, confidence=0) | S | 3.2 | Tests from recordings: two-finger scroll and palm → no events |
| 3.6 | Replay regression suite over all `tests/data/*.json` | S | 2.5, 3.5 | Expected event sequences checked in; CI-runnable |
| 3.7 | `probe gestures`: live recognizer output | S | 3.2 | Manual: 20/20 deliberate swipes recognised; 0 triggers in 2 min of normal centre mousing |

**Risks:** threshold tuning varies by person, so the knobs are exposed in Settings.

---

## P4 — System controls + OSD

| # | Task | Cx | Depends | Acceptance |
|---|---|---|---|---|
| 4.1 | Core Audio COM interop; `VolumeController` Get/Apply/Changed, unmute on raise, default-device re-bind | M | — | Integration test (manual): Apply(+0.1) raises the Windows mixer by 10; plugging in headphones → control follows |
| 4.2 | `BrightnessController` via WMI; IsSupported; throttled worker; read-back event | M | — | 0→100 sweep visibly smooth; E5 latency recorded in both MUX modes |
| 4.3 | `CursorFreezer` (if E3 passed) | S | 2.7, 3.2 | Cursor still during gesture; released on lift, crash or exit |
| 4.4 | WPF `OsdWindow`: no-activate, click-through, topmost, theme-aware, fade | M | — | Shows during gesture in a focused browser, doesn't steal focus or typing, hides after 1.2 s |
| 4.5 | Wire source → recognizer → controllers → OSD (headless host in Probe first) | S | 3.x, 4.1–4.4 | End-to-end: right-edge swipe changes volume and OSD in real time |

**Risks:** WMI on dGPU-only mode (4.2). Exclusive full-screen apps hide the OSD (acceptable).

---

## P5 — Desktop application

| # | Task | Cx | Depends | Acceptance |
|---|---|---|---|---|
| 5.1 | `Sway` WPF exe: single instance, tray (`NotifyIcon`), Enable/Settings/Quit | S | 4.5 | Second launch focuses the first; Quit releases everything |
| 5.2 | Settings window bound to `Settings`, live-applied | M | 3.1 | Change zone width → effect without restart; persisted |
| 5.3 | Launch at startup (HKCU Run) | S | 5.1 | Toggle on → present after sign-out/in |
| 5.4 | Unsupported-hardware and degraded states (no PTP / no WMI) | S | 5.1 | Simulated via flags; correct tray text |
| 5.5 | Logging to `%LOCALAPPDATA%\Sway\log.txt` (size-capped) | S | — | Crash writes a log entry |
| 5.6 | Packaging: self-contained single-file per arch (x64, arm64), zip + per-user installer (no admin) | M | 5.1 | Clean VM install/uninstall leaves no residue apart from settings (and uninstall offers to delete them) |
| 5.7 | Resource budget | S | 5.1 | Idle CPU ~0%, < 60 MB working set, no allocations per frame in the hot path (dotnet-counters) |
| 5.8 | Release workflow: tag → CI builds, tests, signs (SignPath when approved), publishes GitHub Release with checksums; Probe included | M | 5.6, P0 | A tag produces a downloadable release with no manual steps |
| 5.9 | winget manifest; Microsoft Store MSIX submission (v1.x) | S | 5.8 | `winget install` works on a clean VM |
| 5.10 | User docs: README (what it does, GIF, supported hardware, SmartScreen/AV note, privacy statement, uninstall), `docs/HARDWARE.md` | S | 5.1 | A non-developer can install and use Sway from the README alone |

---

## P0 — Public repository setup (do before P2 code lands)

| # | Task | Cx | Acceptance |
|---|---|---|---|
| 0.1 | `git init`; `LICENSE` (MIT), `README.md` (status: research/pre-alpha), `.gitignore`, `.editorconfig` | S | Repo builds empty solution |
| 0.2 | `CONTRIBUTING.md` (build steps, how to record a device, coding rules: no network, no new deps without discussion), `CODE_OF_CONDUCT.md`, `SECURITY.md` (private vulnerability reporting) | S | Present |
| 0.3 | Issue templates: Bug, **Hardware report** (paste `probe report` output), Feature request | S | Template renders on GitHub |
| 0.4 | GitHub Actions CI on `windows-latest`: build x64 + arm64, run `Sway.Core.Tests` (replay tests need no touchpad) | S | Green on PR |
| 0.5 | `THIRD_PARTY_NOTICES.md` | S | Lists any copied or adapted code with its license |

Hardware note: CI runners have no touchpad. Every input-related test must run from **recorded replay data**, which is why `ITouchSource` and `probe record` exist.

---

## Overall estimate

| Phase | Effort |
|---|---|
| P1 remainder | ½ day |
| P2 | 3–4 days |
| P3 | 3–4 days |
| P4 | 4–5 days |
| P5 | 4–5 days |
| P0 + OSS release work (5.8–5.10) | 3–4 days |
| **Total** | **~4–5 engineer-weeks** to a signed, installable v1 for PTP laptops. Medium complexity. The hard parts are P2/P3 tuning across *other people's* hardware and getting signing/reputation, not code volume. |

---

## Acceptance status (2026-11-07)

✅ done · ⏳ needs a hands-on check by a maintainer · ⏸ needs a maintainer decision · ⚠ done with a deviation

| Task | Status | Notes |
|---|---|---|
| 0.1–0.5 | ✅ | Repo public at github.com/ayushgade06/sway; CI green on x64 + arm64 |
| 1.5 | ✅ | `probe caps` / `probe report` match TECHNICAL_RESEARCH §0 exactly; report prints hardware ID only (no instance path, serial or username); exit 2 with "no PTP collection" |
| 1.6 | ⏸ | Needs a pre-release (tag), which needs maintainer approval; then ≥ 5 community reports |
| 2.1 | ✅ | Cursor, tap, two-finger scroll and three-finger gestures normal while Sway runs (manual) |
| 2.2, 2.3 | ✅ / ⏳ | Unit tests (hand-built 30-byte report, hybrid split reports). Live full-span check pending (E1 formal) |
| 2.4, 2.5 | ⚠ | Live view and recorder built. `tests/data/synthetic/` holds generated stand-ins for the 10 scripted gestures until real recordings replace them |
| 2.6 | ✅ / ⏳ | E2 pass (Notepad, browser, full-screen video). E4 (elevated window) result to confirm |
| 2.7 | ✅ | E3 pass: cursor still during the swipe, normal after |
| 2.8 | ⏳ | E6: `probe live` prints scan interval on exit; result pending |
| 3.1–3.6 | ✅ | 43 tests incl. per-transition, mapping, palm, multi-touch, replay suite, zero-allocation check |
| 3.4 | ⚠ | Sensitivity is now "% of pad height" (default 100 % = full height is full range), changed at the maintainer's request after hands-on testing. The optional acceleration curve was not built (YAGNI; not in the settings table) |
| 3.7 | ✅ | 20 deliberate swipes recognised, no accidental triggers during normal use; slide-in from the centre and taps in the zone don't trigger (manual) |
| 4.1 | ✅ | +10 verified on device; headphone re-bind pending a manual check |
| 4.2 | ✅ / ⏳ | Hybrid mode measured (E5). MUX dGPU mode pending. Echo bug fixed |
| 4.3 | ✅ | E3 passed; kept |
| 4.4 | ✅ | Verified by replaying swipes through the packaged app: indicator shows centred at the top, dark theme, speaker/sun icons, fades out; the foreground window never changed (no focus steal) |
| 4.5 | ✅ | Maintainer used edge swipes for volume and brightness end-to-end |
| 5.1 | ✅ | Single instance verified (a second launch opens settings in the first and exits 0) |
| 5.2 | ✅ | Verified through UI Automation: zone width and swipe length changed live, saved to settings.json, kept after restart; Restore defaults works. Found and fixed: checkboxes ignored keyboard/screen-reader toggles |
| 5.3 | ✅ / ⏳ | Toggle writes and removes the HKCU Run value (verified). An actual sign-out/in is still pending |
| 5.4 | ✅ | `--simulate-no-ptp` and `--simulate-no-wmi` verified: settings status text and log report the right state |
| 5.5 | ✅ | `%LOCALAPPDATA%\Sway\log.txt`, 512 KB cap with one rollover; crash handlers log, release the hook, exit |
| 5.6 | ⚠ | Self-contained single-file zips per arch; per-user install via `Sway.exe --install` / `--uninstall` (no installer toolkit, no new dependency). Install/uninstall verified on this PC: files, Start menu shortcut, Apps entry and Run value all removed, folder gone within ~2 s; settings and log kept unless the user says yes. The x64 zip is 92 MB, above the ~30–40 MB estimate. Clean-VM test pending |
| 5.7 | ⚠ | Packaged build: idle CPU 0 ms over 20 s, 47 MB private memory, 97 MB working set (shared .NET/WPF images), above the 60 MB working-set budget. Single-file compression turned off (it cost 75 MB of private memory). Recognizer is allocation-free per frame (tested) |
| 5.8 | ⏸ | Release workflow ready; runs only on a tag push, which needs maintainer approval. Unsigned (D-15) |
| 5.9 | ⏸ | winget manifests prepared in `packaging/winget`, not submitted |
| 5.10 | ✅ | README (install, SmartScreen + checksums, privacy, input transparency, uninstall), HARDWARE.md. The demo GIF still needs to be recorded on the device |
