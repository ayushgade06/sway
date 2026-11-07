# Sway — Product Specification

*Status: draft for approval · 2026-11-07*

## 1. What Sway is

Sway is a small Windows 11 background utility. It turns the left and right edges of a laptop's Precision Touchpad into slider zones:

- **Right edge:** swipe up or down to change the system volume.
- **Left edge:** swipe up or down to change the brightness of the built-in screen.
- **Centre:** the touchpad works exactly as before.

It works like edge sliders on a phone. The change follows your finger: a short swipe makes a small change, a long swipe a big one, and reversing direction mid-swipe reverses the change.

## 2. Interaction model

### 2.1 Zones

```
┌────────┬──────────────────────────────┬────────┐
│        │                              │        │
│ BRIGHT │        normal touchpad       │ VOLUME │
│  ~12%  │                              │  ~12%  │
│        │                              │        │
└────────┴──────────────────────────────┴────────┘
```

- Zone widths are measured in **physical touchpad coordinates** (millimetres from the HID descriptor), never in screen pixels.
- Default width: 12% of the touchpad width per side. Configurable from 5% to 25%. On the reference device (123 mm wide) 12% is about 15 mm, roughly one finger-width.
- Each side can be turned off on its own, and the two sides can be swapped.

### 2.2 Gesture lifecycle

| State | Entry condition | Behaviour |
|---|---|---|
| **Idle** | No contact | — |
| **Candidate** | Exactly one finger touches down **inside** an edge zone | Store the start position. Change nothing yet. |
| **Active** | The finger has moved ≥ activation distance (default 4 mm) vertically, and vertical movement dominates horizontal (angle within ±30° of vertical) | Lock the gesture to that zone's control. Each change in Y maps to a change in level. Show the OSD. |
| **Rejected** | A second finger arrives, horizontal movement dominates, or the contact is marked low-confidence (palm) | Ignore this contact until it lifts. The touchpad keeps behaving normally. |
| **Ended** | The finger lifts | Hide the OSD after the timeout (default 1.2 s). |

Rules:

1. **The touch must start inside the zone** (see DECISIONS D-07). Sliding into a zone from the centre never triggers a gesture.
2. Once a gesture is Active, it stays bound to its control until the finger lifts, even if the finger drifts out of the zone.
3. Mapping is relative and proportional: `Δlevel = −ΔY_mm / (padHeight_mm × travel%)`. Default travel is 100%: a swipe over the full touchpad height covers the full range, on any pad size (changed from 60 mm after hands-on testing, 2026-11-07).
4. Reversing direction during a swipe reverses the change. There is no dead-band, apart from a 0.3 mm jitter filter.
5. Levels are clamped to 0–100%. No wrap-around.

### 2.3 Cursor behaviour during an edge swipe

The Windows touchpad driver keeps moving the cursor whatever Sway does. Sway can't stop the touchpad from producing cursor movement, but it can try to hide that movement. MVP behaviour, pending experiment E3:

- **Target:** while a gesture is Active, the cursor stays still. Sway records the cursor position at touch-down. When the gesture activates, it puts the cursor back there and holds it until lift.
- **Fallback if E3 fails:** the cursor moves vertically during the swipe. This is acceptable for a first build and must be documented to users.

### 2.4 Visual feedback (OSD)

- A small rounded pill near the top-centre of the active monitor, or a vertical bar on the matching side (configurable later; top-centre for MVP).
- Shows an icon (speaker or sun), a fill bar and the percentage.
- Never takes focus, never steals clicks (click-through), and stays above full-screen windows where Windows allows.
- Fades out 1.2 s after the finger lifts.
- Follows the system light/dark theme.

### 2.5 Background operation

- Lives in the system tray. The tray menu has: Enable/Disable, Settings, Quit.
- Optionally starts at login (per-user, no administrator rights).
- No administrator rights are needed at any time.

## 3. Settings (persisted as JSON in `%APPDATA%\Sway\settings.json`)

| Setting | Default | Range |
|---|---|---|
| `enabled` | true | — |
| `leftZone.action` | `brightness` | `brightness` / `volume` / `off` |
| `rightZone.action` | `volume` | same |
| `zoneWidthPercent` | 12 | 5–25 |
| `activationDistanceMm` | 4 | 2–10 |
| `volumeTravelPercent` | 100 (full pad height = full range) | 25–300 % of pad height |
| `brightnessTravelPercent` | 100 | same |
| `freezeCursorDuringGesture` | true | — |
| `osdTimeoutMs` | 1200 | 300–5000 |
| `launchAtStartup` | false | — |

## 4. Constraints and non-goals

**Constraints**

- One-finger gesture is mandatory and is the default.
- Uses physical touchpad coordinates only.
- No kernel driver and no administrator rights for the MVP.
- Must not break normal touchpad behaviour: cursor, tap, two-finger scroll, three- and four-finger system gestures.
- Requires a Windows Precision Touchpad. Legacy Synaptics/Elan "mouse-mode" pads are not supported, and Sway reports this clearly.

**Non-goals for v1**

- External-monitor brightness (DDC/CI). Planned for v1.x.
- Per-app volume.
- Touch screens.
- Gestures other than vertical edge swipes.
- Modifier-key fallback. Specified in DECISIONS D-08 as optional and not built for the MVP.

## 4a. Open-source distribution

Sway is free, MIT-licensed software published on GitHub for anyone to download.

- **Get it:** GitHub Releases (x64 and arm64 zip/installer with checksums), `winget install Sway`, and later the Microsoft Store.
- **Privacy promise:** Sway makes no network connections and collects no telemetry. Touch data never leaves the process and isn't written to disk unless the user explicitly runs a Probe recording.
- **Works on:** Windows 11 laptops with a Precision Touchpad. Compatibility is tracked publicly in `docs/HARDWARE.md`, fed by user-submitted Probe reports.
- **No admin rights** to install, run or uninstall. Uninstall removes everything (it asks before deleting settings).
- **Transparency:** the README explains why Sway reads raw touchpad input and (optionally) installs a mouse hook, so users and antivirus reviewers can verify it in the source.

## 5. Known UX trade-offs

| Trade-off | Why | Mitigation |
|---|---|---|
| Cursor may move during edge swipes | The inbox PTP driver owns cursor generation | Freeze/restore cursor (E3) |
| A normal mousing stroke that **starts** in an edge zone and goes vertical will change volume or brightness | Start-in-zone rule plus vertical dominance | Narrow default zone, 4 mm activation, vertical-angle lock, global toggle |
| No gestures while an elevated (admin) window has focus | Possible UIPI restriction (E4) | Document it. Running Sway elevated is not recommended. |
