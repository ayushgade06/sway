# Sway — Technical Research

*2026-11-07. Legend: **[CONFIRMED-HW]** measured on the reference laptop · **[CONFIRMED-DOC]** official Microsoft docs · **[CONFIRMED-SRC]** read in open-source code · **[INFERRED]** reasoned, not yet tested · **[UNKNOWN]** needs an experiment (see FEASIBILITY_REPORT §4).*

---

## 0. Reference hardware (measured, P1 partly done)

All measured on this machine with PowerShell and a read-only HID probe (`Get-PnpDevice`, `GetRawInputDeviceList`, `HidP_GetCaps`, `HidP_GetValueCaps`, `CreateFile`). No input was captured.

| Item | Value | Tag |
|---|---|---|
| Laptop | Acer Nitro ANV15-51 | CONFIRMED-HW |
| OS | Windows 11, build 26300 | CONFIRMED-HW |
| Touchpad | PixArt **PIXA3848**, I²C-HID (`ACPI\PIXA3848`, service `hidi2c`), Intel Serial IO I²C host | CONFIRMED-HW |
| Driver | Microsoft inbox `input.inf`, 10.0.26100.9818. No vendor driver. | CONFIRMED-HW |
| HID top-level collections | COL01 mouse · **COL02 touch pad (UP:000D U:0005)** · COL03 Input Configuration · COL04 vendor-defined | CONFIRMED-HW |
| PTP report | Report ID 1, 30 bytes, **5 contact link collections (parallel mode)**, contact count max 5 | CONFIRMED-HW |
| X | logical 0–2905 → physical 0–1230, unit 0x11 (SI linear cm), exponent −2 → **12.30 cm** | CONFIRMED-HW |
| Y | logical 0–1766 → physical 0–748 → **7.48 cm** | CONFIRMED-HW |
| Resolution | ~23.6 counts/mm on both axes | CONFIRMED-HW |
| Contact ID | 4 bits (0–15) | CONFIRMED-HW |
| Scan time | 16 bit, units 100 µs | CONFIRMED-HW |
| `CreateFile` on PTP collection, access 0 (query only) | **OK** | CONFIRMED-HW |
| `CreateFile` on PTP collection, `GENERIC_READ` | **Fails, error 32 (ERROR_SHARING_VIOLATION)** | CONFIRMED-HW |
| Built-in display brightness via WMI | `WmiMonitorBrightness` works: current 40, **101 levels** | CONFIRMED-HW |
| .NET SDK | 10.0.402 installed | CONFIRMED-HW |
| PTP settings (HKCU) | taps on, tap-and-drag on, `LeaveOnWithMouse=1`, `EnableEdgy=1` | CONFIRMED-HW |

**Takeaway.** This is the best case: a standard PTP on the inbox driver that exposes the 0x0D/0x05 collection with physical units. The OS holds the HID collection open, so reading it directly is blocked (error 32). Raw Input is the remaining route.

---

## 1. API-by-API evaluation

Columns: **Phys** = gives physical touchpad X/Y · **1F** = covers one-finger mousing · **BG** = works while Sway is in the background · **Coexist** = touchpad keeps working normally · **User-mode** = an ordinary app can use it.

| API | Exposes | Phys | 1F | BG | Coexist | User-mode | Verdict |
|---|---|---|---|---|---|---|---|
| **Raw Input, usage page 0x0D / usage 0x05 with `RIDEV_INPUTSINK`** | Full HID input report for each frame: contact ID, tip, confidence, X, Y, contact count, scan time | **Yes** | **Yes** | **Yes** (INPUTSINK) | **Yes**. Raw Input is a read-only copy; the PTP driver still consumes the reports. | Yes | **Primary mechanism** |
| Raw Input, mouse (0x01/0x02) | Relative mouse deltas after PTP processing (ballistics) | No | — | Yes | Yes | Yes | Useless for position |
| `WM_MOUSEMOVE` / `GetCursorPos` | Screen cursor position | No | — | — | — | Yes | Useless |
| `GetCurrentInputMessageSource` → `IMDT_TOUCHPAD` | Tells you a mouse message came from the touchpad | No | Partly | Foreground only | Yes | Yes | Origin tag only |
| `WM_POINTER` + `GetPointerInfo` | Pointer data for touch/pen. Touchpad only when registered (below). | Not for touchpad mousing | No | No | — | Yes | No |
| **`RegisterTouchpadCapableWindow` + `GetPointerTouchpadInfo`** (Win11) | Touchpad contacts as `POINTER_TOUCH_INFO`, **only for two-finger pan/zoom manipulations**, delivered to the registered window. "Only manipulation gestures are produced; taps and holds are not recognized." Must be called while handling `WM_POINTER`. Mouse messages from one-finger mousing stay mouse messages, tagged `IMDT_TOUCHPAD`. | Contacts, only within a manipulation | **No** | **No** (routed to the target window) | Yes | Yes | **Cannot do Sway.** Wrong gesture class, foreground-routed. |
| `GetPointerInfoHistory` / `GetPointerFrameTouchpadInfoHistory` | History of the above | same | No | No | — | Yes | Same limits |
| Win32 HID (`CreateFile` + `ReadFile` / `HidD_GetInputReport`) on the PTP collection | Raw reports | Yes | Yes | Yes | — | Yes | **Blocked**: sharing violation (error 32, measured) |
| `Windows.Devices.HumanInterfaceDevice` (`HidDevice.FromIdAsync`) | Same as Win32 HID through WinRT | Yes | — | — | — | Yes | **Blocked** [INFERRED: same file-open underneath, and WinRT HID refuses system-owned input usages] |
| Vendor SDK (PixArt) | — | — | — | — | — | — | **None found publicly.** PixArt PTPs ship as plain HID on the inbox driver. |
| UMDF/KMDF HID lower filter | Every report, with the ability to modify or swallow it | Yes | Yes | Yes | Risky | **No**. Needs a signed driver. | Rejected for MVP |
| Virtual HID (vhidmini2-style) | Synthetic devices | — | — | — | — | Driver | Does not solve *reading* |

Sources: [GetPointerTouchpadInfo](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/getpointertouchpadinfo), [RegisterTouchpadCapableWindow](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/registertouchpadcapable), [PTP collection](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection), [PTP sample descriptors](https://learn.microsoft.com/windows-hardware/design/component-guidelines/touchpad-sample-report-descriptors), [About pointer input](https://learn.microsoft.com/en-us/windows/win32/inputmsg/about-pointer-input), [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/raw-input), [UMDF HID minidrivers](https://learn.microsoft.com/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers).

### 1.1 Why GetPointerTouchpadInfo is a trap

The name suggests exactly what Sway needs, but the documentation rules it out [CONFIRMED-DOC]:

- It works only after `RegisterTouchpadCapableWindow` or `RegisterTouchpadCapableThread`. Registering makes the touchpad appear in `GetPointerDevices`, makes touchpad mouse messages report `IMDT_TOUCHPAD`, and gives the window `WM_POINTER` for **two-finger pan and zoom**.
- One-finger contact is still mouse input. Taps and holds are never turned into pointer input.
- Pointer input goes to the target window (the one under the cursor or in focus). A tray app never receives it.

This is the "two-finger gesture handling vs one-finger mousing" distinction the brief asked about. These APIs let a foreground app implement its own pinch and pan. They do not provide global one-finger contact tracking.

### 1.2 Raw Input on the PTP collection: details and pitfalls

- **Registration:** `RAWINPUTDEVICE{ usUsagePage=0x0D, usUsage=0x05, dwFlags=RIDEV_INPUTSINK|RIDEV_DEVNOTIFY, hwndTarget=<message window> }` [CONFIRMED-SRC, see §2].
- **Parsing:** `GetRawInputData(RID_INPUT)` → `RAWHID{dwSizeHid, dwCount, bRawData}` → parse with the HID preparsed data (`GetRawInputDeviceInfo(RIDI_PREPARSEDDATA)`) using `HidP_GetUsageValue` for each `LinkCollection`, and `HidP_GetUsages` / `HidP_GetUsagesEx` for tip switch (0x42) and confidence (0x47).
- **Physical mm:** `mm = (logical − LogicalMin) / (LogicalMax − LogicalMin) × (PhysicalMax − PhysicalMin) × 10^(exp) × 10` (unit cm). **None of the three reference projects does this.** They all use logical values. Sway must, to make zone widths and sensitivity device-independent.
- **Report modes (from the PTP spec):**
  - *Parallel*: all contacts in one report. The reference device works this way (5 link collections) [CONFIRMED-HW].
  - *Hybrid*: contacts spread over several reports, with Contact Count non-zero only in the first. ThreeFingerDrag v2.0.3/2.0.5 added handling for this after user reports [CONFIRMED-SRC]. Sway must handle it for compatibility.
- **Pitfall: `hDevice == NULL`.** Community reports describe built-in touchpads delivering `WM_INPUT` with a null `hDevice` [Stack Overflow 57552844](https://windows-hexerror.linestarve.com/q/so57552844-rawinputheader-hdevice-null-on-wminput-for-laptop-trackpad), [MS Q&A](https://learn.microsoft.com/en-za/answers/questions/1666513/getrawinputdata-header-hdevice-is-null-for-builtin). Mitigation: if the handle is null, use the preparsed data of the single PTP found at enumeration [INFERRED]. Must be checked on the reference device in P2.
- **Pitfall: mouse-mode-only pads.** Some PTPs report through the mouse collection until Windows enables PTP mode. A pad with no 0x0D/0x05 collection is not a PTP, and Sway cannot work on it in user mode.
- **Pitfall: palm.** Use the confidence bit (0x47). The PTP spec requires devices to clear it for palms or large objects.
- **Pitfall: touchpad disabled.** If the user turns the touchpad off (Fn key or Settings), no reports arrive. This is correct behaviour.
- **Background delivery.** `RIDEV_INPUTSINK` delivers `WM_INPUT` "even when the caller is not in the foreground" (Raw Input docs). Two shipped apps rely on it for the PTP collection [CONFIRMED-SRC].
- **[UNKNOWN] Elevated foreground window (UIPI).** Whether a medium-integrity sink still receives PTP `WM_INPUT` while an elevated app has focus. Experiment E4.

### 1.3 Exclusivity summary

| Path | Result |
|---|---|
| Raw Input subscription | Shared, read-only copy. No exclusivity issue [CONFIRMED-SRC + design of Raw Input] |
| Opening the HID collection for read | **Sharing violation** [CONFIRMED-HW] |
| Swallowing touchpad reports so no cursor/tap happens | Impossible from user mode. Needs a filter driver [CONFIRMED-DOC, filter architecture] |
| Swallowing the *resulting* mouse events | Possible in principle with a `WH_MOUSE_LL` hook. Whether PTP-synthesised moves reach LL hooks is **[UNKNOWN]**: community sources disagree. Experiment E3. |

---

## 2. Existing open-source projects (verified against source)

| Repo | Lang / arch | Does | Phys 1F coords | Driver | Reusable | Relevance |
|---|---|---|---|---|---|---|
| [kamektx/TouchpadGestures_Advanced](https://github.com/kamektx/TouchpadGestures_Advanced) (MIT) | C# WPF + C++ DLL | Custom multi-finger PTP gestures | Logical X/Y per contact. Reads tip (0x42) and confidence (0x47) via `HidP_GetUsagesEx`. Detects hybrid mode explicitly. | No | Parsing approach, hybrid detection | **High**. Most complete parser. `RIDEV_INPUTSINK` in `TGA_Win32/RegisterRawInput.cpp`. |
| [emoacht/RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad) (MIT) | C# WPF, P/Invoke | Displays contacts | Logical only. No tip/confidence. Ignores `dwCount > 1`. | No | Minimal P/Invoke reference | Medium. **`dwFlags = 0`, foreground only** (comment in source). Not enough for Sway as written. |
| [ClementGre/ThreeFingerDragOnWindows](https://github.com/ClementGre/ThreeFingerDragOnWindows) (MIT) | C# WinUI 3 | macOS-style three-finger drag, shipped app | Logical. Handles `dwCount` loop and sequential/hybrid reports (`ContactsManager.cs`). | No | Hybrid-report heuristics | **High**. Shows a shipped .NET background app reading PTP with `dwFlags = 0x2100` (INPUTSINK + DEVNOTIFY). No cursor-suppression hook found in the files read. |
| [EsportToys/PrecisionThreeFingerDrag](https://awesome.ecosyste.ms/projects/github.com%2Fesporttoys%2Fprecisionthreefingerdrag) | AutoIt | Same idea | Raw Input | No | — | Low. Corroborates the approach. |
| [imbushuo/mac-precision-touchpad](https://github.com/imbushuo/mac-precision-touchpad) | C, KMDF/UMDF | PTP driver for Apple trackpads | — | **Yes** | — | Shows what the driver route costs (signing, per-transport drivers). Not for Sway. |
| [microsoft/Windows-driver-samples](https://github.com/microsoft/Windows-driver-samples) `hid/vhidmini2` | C, KMDF/UMDF | HID minidriver/filter sample | — | Yes | — | Reference only, for a possible future filter provider |
| [xanderfrangos/twinkle-tray](https://github.com/xanderfrangos/twinkle-tray) (MIT) | Electron/Node | Brightness tray app, WMI + DDC/CI | — | No | Pattern for v1.x external monitors | Medium |
| EarTrumpet (File-New-Project/EarTrumpet) | C# WPF | Volume tray app with Core Audio | — | No | Pattern for COM interop, flyout UX | Medium [not source-verified in this pass] |

**No project found that does edge-zone one-finger volume/brightness on a PTP.** The pieces exist separately. Nothing can be used as a whole.

**Common gap:** every project uses logical coordinates and none handles physical units. Only kamektx reads confidence. None suppresses cursor movement.

---

## 3. System control

### 3.1 Volume — Core Audio [CONFIRMED-DOC]

- `IMMDeviceEnumerator.GetDefaultAudioEndpoint(eRender, eMultimedia)` → `IMMDevice.Activate(IID_IAudioEndpointVolume)` → `IAudioEndpointVolume`.
- `GetMasterVolumeLevelScalar` / `SetMasterVolumeLevelScalar(float 0..1, eventContext)`. The scalar is perceptually tapered (audio-taper), so a linear finger mapping feels right.
- `RegisterControlChangeNotify(IAudioEndpointVolumeCallback)` reports external changes, so the OSD always starts from the true value. Pass Sway's own GUID as `eventContext` to ignore its own echoes.
- `IMMNotificationClient.OnDefaultDeviceChanged` lets Sway re-bind when headphones are plugged in.
- Setting a level does **not** show Windows' own volume flyout, so Sway's OSD is needed.
- Unmute automatically when raising from 0 (`SetMute(false)`).
- Fast (in-process COM). It can be called at the touchpad frame rate.
- .NET: hand-written `[ComImport]` interfaces, about 80 lines. **No NuGet** (NAudio would be about 1 MB for four calls).
- Docs: [IAudioEndpointVolume](https://learn.microsoft.com/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume).

### 3.2 Brightness

| Mechanism | Internal panel | External monitor | Rights | Notes |
|---|---|---|---|---|
| **WMI `root\wmi` `WmiMonitorBrightnessMethods.WmiSetBrightness(timeout, level)`** + `WmiMonitorBrightness` (read) + `WmiMonitorBrightnessEvent` | **Yes. Works on the reference device (101 levels)** [CONFIRMED-HW read] | No | None (standard user) [CONFIRMED-DOC; write CONFIRMED in P4] | Calls take tens of ms [INFERRED]. **Must throttle** (coalesce to the latest value, max ~20–30 Hz on a worker thread). |
| Dxva2 `SetMonitorBrightness` (DDC/CI, Monitor Configuration API) | No (laptop panels don't do DDC/CI) | Yes, if the monitor supports DDC/CI | None | Slow (50–100 ms per call), I²C. v1.x. |
| `IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS` on `\\.\LCD` | Yes | No | **Admin** | Rejected |
| `Windows.Graphics.Display.BrightnessOverride` | Only from a UWP foreground view | No | — | Rejected |
| Simulated brightness keys | No standard virtual key exists for brightness | — | — | Not possible |

Also note: OEM/adaptive brightness (CABC, Windows content-adaptive brightness) can fight manual changes. Document this. WMI calls may fail on some hybrid-GPU (MUX-switch) configurations when the panel is driven by the dGPU [INFERRED]. The Nitro ANV15 has a MUX switch, so this is a test case for P4.

.NET access: `System.Management` (Microsoft package, about 0.5 MB) or a raw COM `IWbemServices` binding. Recommend `System.Management`: one first-party dependency, mature.

### 3.3 OSD / overlay options

| Option | Transparency / click-through | No-activate | Effort | Runtime |
|---|---|---|---|---|
| **WPF window** `AllowsTransparency`, `WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_TOPMOST`, `ShowActivated=false` | Easy | Easy | Low | In .NET Desktop runtime |
| WinUI 3 | Per-pixel transparent top-level windows are awkward (needs `DesktopWindowXamlSource` / composition hacks). Mica is for main windows. | Possible via HWND interop | High | Windows App SDK runtime too |
| Raw Win32 + `UpdateLayeredWindow` / DirectComposition | Best | Yes | High | — |

Exclusive full-screen games cover any overlay. Borderless "full-screen" windows do not.

---

## 4. UX research notes

- **Start-in-zone vs enter-zone.** Phones use start-at-edge (system edge swipe). Enter-zone would trigger on every vertical mousing stroke near the edge of the pad, which is a very common motion. Start-in-zone wins clearly (DECISIONS D-07).
- **Activation distance.** Windows' own tap/drag thresholds are a few millimetres. 4 mm (about 95 counts here) separates a deliberate swipe from a tap or rest jitter.
- **Direction lock.** Require |ΔY| ≥ 2·|ΔX| at activation (about 27° from vertical). After activation, ignore X.
- **Taps in zone.** A tap is touch-down then lift with < activation distance. Sway never activates on it, and Windows' own tap-click is untouched.
- **Multi-touch.** Any second contact before activation → Rejected (the user is scrolling or doing a system gesture). A second contact after activation → end the gesture, so two-finger scroll is never disturbed.
- **Windows edge behaviour.** Windows already ignores some edge contact for palm rejection (`AAPThreshold`). Sway reads raw reports *before* that filtering, so it sees edge contacts Windows might discard. That is good for Sway, and it means Sway must do its own palm check (confidence bit + contact-size heuristics if available).

---

## 5. Open questions → experiments

See FEASIBILITY_REPORT §4 (E1–E6).
