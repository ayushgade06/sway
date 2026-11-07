# Sway — Feasibility Report

*2026-11-07 · Reference device: Acer Nitro ANV15-51, PixArt PIXA3848 PTP, Windows 11 build 26300.*

## 1. Direct answers

| # | Question | Answer | Confidence |
|---|---|---|---|
| 1 | Can an ordinary Windows 11 app obtain physical one-finger touchpad coordinates? | **Yes, via Raw Input on the PTP HID collection (0x0D/0x05).** The reports carry per-contact X/Y in device units, and the descriptor gives the physical size (reference: 2905×1766 counts = 123.0×74.8 mm). One-finger contacts are included: they are ordinary PTP frames. | **High (90%)**. The descriptor is measured on the device. Three open-source projects parse these reports. Live capture on this device not yet observed (E1). |
| 2 | Globally, while another app is focused? | **Yes, with `RIDEV_INPUTSINK`.** Documented Raw Input behaviour, used by two shipped PTP apps. | **High (85%)**. Exception to verify: elevated foreground windows (E4). |
| 3 | Can it coexist with normal PTP behaviour? | **Yes for reading.** Raw Input is a passive copy, so the touchpad keeps working. **But Sway cannot stop the touchpad from moving the cursor**, because the inbox driver acts on the same contact. Hiding that movement in user mode depends on E3. | Reading: **High (90%)**. Cursor suppression: **Medium (50%)**. |
| 4 | Which APIs can / cannot? | **Can:** Raw Input 0x0D/0x05. **Cannot:** `GetPointerTouchpadInfo` and `RegisterTouchpadCapableWindow` (two-finger manipulations only, sent to the target window), `WM_POINTER` / `GetPointerInfo` (no touchpad mousing), mouse messages / raw mouse / `GetCursorPos` (screen-space deltas only), Win32 HID `ReadFile` and WinRT `HidDevice` (sharing violation, error 32, **measured**), vendor SDK (none exists). | **High (90%)** |
| 5 | Is a custom driver necessary? | **No, for the MVP.** A driver would only be needed to *consume* edge contacts so the cursor never moves and no edge taps happen. That is a nice-to-have, and it costs EV signing, attestation and a real risk of breaking the touchpad. | **High (85%)** |
| 6 | Most practical route to an MVP? | .NET 10 WPF tray app. Raw Input message-only window with `RIDEV_INPUTSINK`. Own HID parser using physical units and hybrid-mode support. Pure gesture state machine. Core Audio via COM interop. WMI brightness (throttled). WPF no-activate OSD. Optional `WH_MOUSE_LL` cursor freeze if E3 passes. | **High (80%)** |

## 2. Biggest blocker

**There isn't a hard blocker. The biggest compromise is cursor movement during edge swipes.**

Sway gets a copy of each frame. The PTP driver gets the original and moves the cursor at the same time. Options, in order of preference:

1. `WH_MOUSE_LL` hook drops mouse moves while a gesture is Active, plus `SetCursorPos` back to the touch-down position at activation. Works only if PTP-synthesised moves pass through LL hooks. Community sources conflict, so this is **E3**.
2. If E3 fails: `SetCursorPos` to the anchor on every frame while the gesture is Active. This gives a visible jitter.
3. If that is unacceptable: accept vertical cursor drift.
4. Last resort (v2+, only if users demand it): a UMDF2 HID lower filter. See DECISIONS D-03.

## 3. Compatibility risk

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Device isn't a PTP (no 0x0D/0x05 collection) | Low on 2020+ Win11 laptops | Total | Detect at start. Clear "unsupported touchpad" message in the diagnostic. |
| Hybrid-mode reporting | Medium (varies by vendor) | Wrong contacts | Implement hybrid assembly (kamektx/ThreeFingerDrag show it is needed). Replay tests. |
| `hDevice == NULL` in WM_INPUT | Medium (reported for built-in pads) | Can't look up preparsed data | Fall back to the single enumerated PTP. Test in E1. |
| No physical units in descriptor | Low (spec requires them) | Zones in % only | Fall back to logical-percentage zones. |
| WMI brightness unsupported or slow (MUX/dGPU mode, OEM) | Medium | Left zone dead | Detect at start; disable left zone with a tray notice. Test both MUX modes (E5). |
| UIPI: no WM_INPUT while an elevated app is focused | Unknown | Gestures pause | Document. Optional "run elevated" not recommended. (E4) |
| Windows update changes PTP raw delivery | Low | Total | The approach has worked since Windows 10. Track Insider builds; the reference device is already on 26300. |
| **OSS: SmartScreen warnings on unsigned downloads** | Certain until signed | Many users won't install | Free OSS signing (SignPath) or Store signing before 1.0 (DECISIONS D-15) |
| **OSS: antivirus false positives** (global mouse hook + input capture looks like a keylogger) | Medium | Release flagged or quarantined | Signing, no packers/obfuscation, cursor freeze is a visible documented setting, Defender false-positive submissions |
| **OSS: hardware the maintainers don't own** | Certain | Bugs nobody can reproduce | Probe `report`/`record` + replay regression tests (D-17) |
| **OSS: maintainer bandwidth** | Medium | Stale issues | Small codebase, one dependency, CI-tested replay data, clear CONTRIBUTING |

## 4. Minimal experiments (all part of P1/P2, throwaway code in `tools/`)

| ID | Question | Method | Pass criterion |
|---|---|---|---|
| **E1** | Do PTP frames arrive on this device? Is `hDevice` valid? Parallel or hybrid? | Console app with a message-only window. Register 0x0D/0x05 + INPUTSINK. Log each frame: hDevice, dwCount, contact count, (id, tip, conf, x, y). | One-finger frames show tip=1 and a moving X/Y for a slow left-to-right swipe, reaching ≈0 and ≈2905. |
| **E2** | Background delivery | Same tool. Focus Notepad, a browser, a full-screen video. | Frames keep arriving in all three. |
| **E3** | Can PTP cursor movement be held still from user mode? | Add `WH_MOUSE_LL`. Log `flags`/`dwExtraInfo` for touchpad moves. While a key is held, return 1 for moves. | Cursor stays still while the touchpad keeps sending frames. Taps and scrolling are unaffected after release. |
| **E4** | Elevated foreground | Run Task Manager elevated and focus it. | Record whether frames still arrive (documents a limitation either way). |
| **E5** | Brightness latency and MUX | Time 100 `WmiSetBrightness` calls in hybrid and discrete-GPU modes. | Success in both. p95 latency measured to set the throttle rate. |
| **E6** | Frame rate and latency | Scan-time deltas over a swipe. | ≥ 100 Hz expected for an I²C PTP. Use the result to tune the jitter filter. |

## 5. Recommendation

### **CONDITIONAL GO**

Feasible in user mode with no driver, based on documented APIs, measured hardware capabilities and three source-verified projects using the same mechanism. The conditions are E1 + E2 passing on the reference device, which is a half-day experiment. E3 decides whether the cursor stays still or drifts during a gesture; it does not decide whether Sway is viable.

## 6. Results (reference device, 2026-11-07)

| ID | Result | Evidence |
|---|---|---|
| Layout | **Pass.** `Sway.Probe caps` reads the live descriptor: report ID 1, 30 bytes, 5 slots, parallel; X 0..2905 → 123.0 mm, Y 0..1766 → 74.8 mm; field bit offsets found by HidP_Set* probing (tip @9, conf @8, id @10:4, X @16:16, Y @32:16 per 40-bit slot; contact count @208:8; scan time @224:16). | `Sway.Probe caps` output |
| E1 | **Pass (informal).** The maintainer used the tray app's edge swipes on this laptop: frames arrive, one-finger coordinates are parsed, edge zones trigger volume and brightness. A formal `Sway.Probe live` run (full X span 0 → 123 mm, null-hDevice count) is still UNKNOWN. | Hands-on test of the app build |
| E2 | **Pass.** Edge swipes worked with Notepad, a browser and full-screen video focused. | Maintainer manual test, 2026-11-07 |
| E3 | **Pass.** With "keep the cursor still" on, the cursor stays put during an edge swipe and moves normally after lift: PTP-synthesised moves do pass through `WH_MOUSE_LL`. CursorFreezer stays (D-09). | Maintainer manual test, 2026-11-07 |
| E4 | UNKNOWN: elevated Task Manager focused. | — |
| E5 (hybrid GPU mode) | **Pass.** 100 × `WmiSetBrightness`: median 5.5 ms, p95 8.7 ms, max 54 ms, 0 failures. The throttle is set to 16 ms. Read-back events (`WmiMonitorBrightnessEvent`) arrive late and echo Sway's own writes; Sway ignores read-backs for 1 s after it writes (otherwise the level snapped back after release, a bug found in hands-on testing). | `Sway.Probe brightness` |
| E5 (discrete GPU / MUX) | UNKNOWN: switch the MUX to dGPU-only, reboot, run `Sway.Probe brightness`. | — |
| E6 | UNKNOWN: `Sway.Probe live` prints the scan interval on exit. | — |
| Volume (P4.1) | **Pass.** `Apply(+0.1)` moved the Windows volume 74 → 84 and back; 0.2 ms per call. | `Sway.Probe volume` |
