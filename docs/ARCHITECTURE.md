# Sway — Architecture

*Proposed. Revised from the brief: fewer layers, one interface where a second implementation is real (replay for tests), and nothing speculative.*

## 1. Shape

Two production projects and one throwaway tool:

```
src/
  Sway.Core/          # net10.0 class library. No UI, no WPF. Unit-testable.
    Input/            # Raw Input registration, HID parsing → TouchFrame
    Gestures/         # EdgeGestureRecognizer (pure state machine)
    Controls/         # VolumeController (Core Audio COM), BrightnessController (WMI)
    Settings.cs       # record + JSON load/save (System.Text.Json)
  Sway/               # net10.0-windows WPF exe (UseWPF + UseWindowsForms for NotifyIcon)
    App.xaml(.cs)     # tray, wiring, single-instance
    OsdWindow.xaml    # overlay
    SettingsWindow.xaml
tests/
  Sway.Core.Tests/    # xUnit: parser against captured reports, recognizer against recorded frames
tools/
  Sway.Probe/         # diagnostic console app (E1–E6); also shipped to users for hardware reports
tests/data/           # recorded touch sessions per device: the cross-hardware regression suite
.github/              # CI workflow, release workflow, issue templates (bug / hardware report)
LICENSE  README.md  CONTRIBUTING.md  SECURITY.md  THIRD_PARTY_NOTICES.md
```

Open-source constraints that shape this layout:
- Everything input-related must be testable **without a touchpad** (CI runners, contributors without the device), so the replay source and recorded data are first-class.
- `Sway.Core` contains no network code (D-16).
- New runtime dependencies need an MIT-compatible license and a discussion first.

## 2. Diagram

```mermaid
flowchart LR
  subgraph OS["Windows"]
    PTP["PTP HID device<br/>(PIXA3848, inbox driver)"]
    HIDSTACK["HID class / PTP stack"]
    CURSOR["Cursor, taps, gestures<br/>(unchanged)"]
    RI["Raw Input<br/>WM_INPUT copy"]
    LL["WH_MOUSE_LL<br/>(optional, E3)"]
    AUD["Core Audio<br/>IAudioEndpointVolume"]
    WMI["WMI root\\wmi<br/>WmiMonitorBrightness*"]
  end

  subgraph Core["Sway.Core"]
    SRC["RawInputTouchSource<br/>message-only HWND<br/>RIDEV_INPUTSINK"]
    PARSE["PtpReportParser<br/>physical mm, hybrid assembly"]
    REC["EdgeGestureRecognizer<br/>Idle→Candidate→Active→Ended"]
    VOL["VolumeController"]
    BRI["BrightnessController<br/>(throttled worker)"]
    FRZ["CursorFreezer"]
  end

  subgraph App["Sway (WPF)"]
    TRAY["Tray + Settings"]
    OSD["OsdWindow"]
  end

  PTP --> HIDSTACK --> CURSOR
  HIDSTACK --> RI --> SRC --> PARSE -->|TouchFrame| REC
  REC -->|LevelDelta(zone, Δ)| VOL --> AUD
  REC -->|LevelDelta| BRI --> WMI
  REC -->|Active / Ended| FRZ --> LL
  VOL -->|level changed| OSD
  BRI -->|level changed| OSD
  TRAY -->|Settings| REC
```

## 3. Components and contracts

### 3.1 Input

```csharp
public readonly record struct Contact(int Id, bool Tip, bool Confident, float Xmm, float Ymm);
public readonly record struct TouchFrame(long TimestampTicks, IReadOnlyList<Contact> Contacts, float WidthMm, float HeightMm);

public interface ITouchSource : IDisposable
{
    event Action<TouchFrame> Frame;   // raised on the input thread
}
```

- `RawInputTouchSource` owns a dedicated STA thread with a message-only window (`HWND_MESSAGE`). It registers 0x0D/0x05 with `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`, pumps `WM_INPUT`, and handles `WM_INPUT_DEVICE_CHANGE` (pad re-enumerated after sleep).
- `PtpReportParser` gets its value and button caps once per device. It converts logical to mm using physical range and unit exponent, and assembles hybrid reports (contact count ≠ 0 starts a frame; collect until N contacts).
- `ReplayTouchSource` (tests and Probe) plays recorded frames from JSON. This second implementation is the reason `ITouchSource` exists.
- A future driver-backed or vendor source would also implement `ITouchSource`. Nothing is built for that now.

### 3.2 Gestures

```csharp
public enum Zone { None, Left, Right }
public sealed class EdgeGestureRecognizer
{
    public EdgeGestureRecognizer(GestureOptions options);
    public IEnumerable<GestureEvent> Process(TouchFrame frame); // pure: no I/O, no clock reads
}
public abstract record GestureEvent;
public sealed record GestureStarted(Zone Zone) : GestureEvent;
public sealed record LevelDelta(Zone Zone, float Delta) : GestureEvent;   // fraction of full range, signed
public sealed record GestureEnded(Zone Zone) : GestureEvent;
```

It is deterministic and gets time from the frame, so it can be fully tested from recorded data. Rules are in PRODUCT_SPEC §2.2.

### 3.3 Controls

No interface. Each is a single concrete class.

- `VolumeController`: `float Get()`, `float Apply(float delta)` (clamped, unmutes on raise), `event Action<float> Changed` (from the endpoint callback, ignoring its own context GUID). Re-binds on default-device change.
- `BrightnessController`: `bool IsSupported`, same API. `Apply` updates a target value only. A single worker sends the latest target to WMI, at most once every ~40 ms (tuned by E5). Reads back on `WmiMonitorBrightnessEvent`.

### 3.4 CursorFreezer (optional, gated by E3)

On `GestureStarted` it records the anchor, calls `SetCursorPos(anchor)` and installs or enables a `WH_MOUSE_LL` hook that drops non-injected moves. On `GestureEnded` it releases. The hook runs on the input thread. It must return within the system LL timeout (~300 ms default, registry-tunable) or Windows silently removes it. The callback only checks a volatile bool.

### 3.5 App

- `App` handles single-instance (named mutex) and wiring: source → recognizer → controllers → OSD.
- The UI is marshalled with `Dispatcher.InvokeAsync`, and OSD updates are coalesced per render frame.
- Tray: WinForms `NotifyIcon` (in-box, no NuGet).
- Startup: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value. This is unpackaged. If the app is later MSIX-packaged, switch to `StartupTask`.
- Settings: `System.Text.Json`, `%APPDATA%\Sway\settings.json`. Unknown or invalid values fall back to defaults.

## 4. Threading

| Thread | Work |
|---|---|
| Input thread (STA, message loop) | WM_INPUT → parse → recognize → `VolumeController.Apply` (fast COM) and `BrightnessController` target update. LL hook callback. |
| Brightness worker | WMI calls |
| UI thread (WPF) | OSD, tray, settings |

Input is kept off the UI thread, so a busy UI can't delay touch frames or the LL hook.

## 5. Failure handling

| Failure | Behaviour |
|---|---|
| No PTP collection at start | Tray icon in "unsupported" state; balloon text explains it; app keeps running for settings. |
| Pad disappears (sleep, disable) | `WM_INPUT_DEVICE_CHANGE` → drop caps; re-parse on arrival. |
| Malformed or short report | Drop the frame and count it (diagnostic counter). Never throw on the input thread. |
| Hybrid frame never completes | Discard after 2 frame periods. |
| Audio endpoint gone | Re-bind on the next default device. Volume zone no-ops until then. |
| WMI fails | `IsSupported=false`; left zone disabled; tray notice once. |
| LL hook removed by the OS (timeout) | Detect (no callbacks while frames show movement) and reinstall once; otherwise disable the freeze for the session. |
| Unhandled exception | Log to `%LOCALAPPDATA%\Sway\log.txt`, release the hook, exit cleanly. The touchpad is never affected, because Sway only reads. |

**Safety property:** Sway cannot break the touchpad. Without the optional hook, it never alters input. The hook only drops moves while a gesture is Active, and the OS removes the hook automatically if Sway hangs.

## 6. Dependencies

| Need | Choice | Why |
|---|---|---|
| Raw Input, HID parsing, hooks | P/Invoke (`LibraryImport`) to user32/hid.dll | In-box |
| Volume | `[ComImport]` Core Audio | In-box; NAudio unnecessary |
| Brightness | `System.Management` | First-party; WMI method calls |
| JSON | `System.Text.Json` | In-box |
| Tray | WinForms `NotifyIcon` | In-box with `UseWindowsForms` |
| Tests | xUnit | Standard |

Total third-party runtime dependencies: **one** (`System.Management`, Microsoft).

## 7. Extensibility (not built, only kept possible)

- More actions per zone (media scrub, zoom): add values to the `ZoneAction` enum plus a controller.
- External monitors: a DDC/CI path inside `BrightnessController`.
- Driver-backed input: a new `ITouchSource`.
- Horizontal top/bottom-edge zones: the recognizer already reasons in mm. Adding them means adding a zone predicate.
