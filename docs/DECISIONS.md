# Sway — Decision Log

Format: decision · alternatives considered · reason · status.

### D-01 Input mechanism: Raw Input on the PTP collection (0x0D/0x05) with `RIDEV_INPUTSINK`
- **Alternatives:** `GetPointerTouchpadInfo`/`RegisterTouchpadCapableWindow`; WM_POINTER; mouse messages/raw mouse; Win32 HID ReadFile; WinRT HidDevice; vendor SDK; driver.
- **Why:** it is the only user-mode path that gives physical per-contact coordinates for one-finger contact while in the background. The pointer touchpad APIs only produce two-finger manipulations and send them to the target window. Direct HID open fails with a sharing violation (measured). No PixArt SDK exists.
- **Status:** Proposed, pending E1/E2.

### D-02 No interference with the PTP stack for reading
- Sway only reads a copy. Nothing it does can break the touchpad. This is the safety baseline.

### D-03 No custom driver for v1
- **Alternatives:** UMDF2 HID lower filter under MsHidUmdf (vhidmini2 base); KMDF filter; virtual HID device.
- **Why rejected:** the only extra it provides is consuming edge contacts (no cursor drift, no edge taps). The costs are EV certificate + Microsoft attestation signing, a per-device INF match, the risk of a non-working touchpad on a bug, Windows Update driver interactions, and Secure Boot/HVCI constraints. A virtual HID device doesn't help reading at all.
- **Revisit if:** E3 fails *and* users reject cursor drift. Then a UMDF2 filter is preferred over KMDF (crash-isolated).

### D-04 Physical millimetres, not logical units or screen pixels
- **Alternatives:** logical units (all reference projects); percentage of logical range.
- **Why:** zone width and swipe sensitivity should feel the same on a 9 cm and a 15 cm pad. The descriptor provides the physical range (measured: 123.0×74.8 mm). Percentage of the logical range is the fallback when units are missing.

### D-05 UI: WPF (+ WinForms NotifyIcon) over WinUI 3
- **Alternatives:** WinUI 3; raw Win32/DirectComposition.
- **Why:** the OSD needs a transparent, click-through, no-activate top-level window. That is trivial in WPF and awkward in WinUI 3. WinUI 3 adds the Windows App SDK runtime to deploy, and Sway's settings UI is too small to benefit. WPF runs on .NET 10 desktop runtime and starts quickly. Raw Win32 gives no meaningful gain for the extra effort.
- **Trade-off:** WPF looks less "Windows 11 native". It can be themed (rounded corners, Mica via DWM attributes on the settings window).

### D-06 Volume via hand-written Core Audio COM interop; brightness via WMI (`System.Management`)
- **Alternatives:** NAudio / AudioSwitcher (volume); simulated VK_VOLUME keys (2% steps, not proportional, triggers the Windows flyout); `IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS` (admin); DDC/CI (external only).
- **Why:** proportional, no admin rights, minimal dependencies. WMI brightness is confirmed present on the reference device (101 levels). WMI calls are throttled because they are slow.

### D-07 Gesture must start inside the edge zone
- **Alternative:** activate when an ongoing stroke enters the zone.
- **Why:** vertical mousing near the pad edges is common. Enter-zone activation would cause frequent accidental changes and would turn normal cursor motion into a volume change mid-stroke. Start-in-zone matches phone edge gestures and makes intent explicit at touch-down. Combined with the 4 mm activation and 2:1 vertical-dominance lock, this is the main defence against accidental triggers.
- **Cost:** a user has to place the finger deliberately at the edge. This is acceptable and expected.

### D-08 Modifier-key fallback: specified, not built
- **Option:** hold a configurable key (e.g. Alt) to make edge swipes active, or to make *any* vertical swipe adjust.
- **Decision:** not in the MVP. It is the planned escape hatch if field testing shows too many accidental triggers on some hardware. It fits the recognizer as an extra activation predicate. The default interaction stays one-finger, no modifier.

### D-09 Cursor freeze via `WH_MOUSE_LL` + anchor restore, conditional on E3
- **Alternatives:** per-frame `SetCursorPos` (jittery); accept drift; driver (D-03).
- **Why:** this is the only user-mode way to hide touchpad-driven cursor movement. It only works if PTP-synthesised moves pass through LL hooks, which community reports conflict on, hence E3. The hook callback only checks a flag, so it can't trip the OS LL-hook timeout.

### D-10 Dedicated input thread
- **Why:** WM_INPUT handling and the LL hook must not wait behind WPF layout/rendering. A late LL hook is removed by Windows, and late frames cause laggy gestures.

### D-11 Interfaces only where two implementations exist
- `ITouchSource` (raw input + replay for tests). Controllers, recognizer and settings are concrete classes. **Why:** the brief's provider/vendor/driver layering is kept possible through `ITouchSource` without building speculative layers.

### D-12 Unpackaged, per-user, no admin
- HKCU Run for startup, `%APPDATA%` for settings. **Why:** zero-friction install. Nothing in Sway needs elevation.

---

## Open-source distribution decisions

Sway is a public open-source project that strangers download and run. These decisions follow from that.

### D-13 License: MIT
- **Alternatives:** GPL-3.0 (keeps forks open, but deters reuse and store distribution); Apache-2.0 (patent grant, more ceremony).
- **Why:** every reference project we learn from (kamektx, emoacht, ThreeFingerDrag, Twinkle Tray) is MIT, and so is `System.Management`. MIT keeps compatibility simple. Rule: **no code is copied from a reference project without keeping its copyright notice in `THIRD_PARTY_NOTICES.md`.** The plan is to write our own parser, guided by the HID spec, so ideally nothing is copied.

### D-14 Distribution channels: GitHub Releases + winget, Microsoft Store later
- **GitHub Releases** (primary): zip + installer per architecture, SHA-256 checksums, built by CI from a tag. No hand-built binaries.
- **winget** manifest submitted for each release (`winget install Sway`).
- **Microsoft Store (MSIX), v1.x:** the Store signs packages for free and individual developer accounts are free. This is the cheapest way to get trusted signing and clean updates.
- **Rejected:** a custom auto-updater in v1. That adds attack surface and code. The Store and winget handle updates. Add a "new version available" check (GitHub API, opt-in) only if users ask.

### D-15 Code signing: required before 1.0; pursue free OSS signing
- **Problem:** unsigned exes get SmartScreen "Windows protected your PC" warnings. A tray app with a global mouse hook and raw-input capture also looks like a keylogger to antivirus heuristics. For an OSS project with no reputation, this is the biggest adoption risk.
- **Options, in order:**
  1. **SignPath Foundation**: free Authenticode signing for OSS, which signs CI-built artifacts only.
  2. **Microsoft Store** signing (D-14).
  3. Azure Artifact/Trusted Signing (low monthly cost; identity validation needed).
- Pre-1.0 previews may ship unsigned, with the README explaining the SmartScreen prompt and checksums.
- **Antivirus hygiene:** no obfuscation, no packers, no runtime-downloaded code, and the hook/raw-input purpose documented in the README. Submit releases to Microsoft Defender's false-positive portal if flagged.

### D-16 Privacy: no network, no telemetry, ever by default
- Sway sees every touchpad movement, so trust is the product. **The app makes zero network calls** in v1. Logs stay local, contain no coordinates by default, and are size-capped.
- Hardware compatibility data is collected **only** when the user runs `Sway.Probe` and pastes its report into a GitHub issue (D-17).
- The README states this plainly. CI can enforce it with a test that the main assembly references no `System.Net.Http` types.

### D-17 Hardware compatibility is crowd-sourced through the Probe tool
- **Why:** maintainers have one laptop. Users have hundreds of touchpads (Synaptics, Elan, PixArt, Goodix, Apple via drivers, Surface, Snapdragon laptops).
- `Sway.Probe` ships in every release. `probe report` prints a paste-ready Markdown block: model, touchpad VID/PID, driver, collection layout (parallel or hybrid), physical size, frame rate. It contains no personal data, and the user reviews it before posting.
- A GitHub issue template "Hardware report" collects these. `docs/HARDWARE.md` lists works / partial / broken devices.
- Optional recordings (`probe record`) can be attached so problem devices become **replay regression tests**. Contributors fix device bugs without owning the device.

### D-18 Architectures: win-x64 and win-arm64
- Windows-on-Arm laptops (Snapdragon X) all ship Precision Touchpads and are a natural audience. .NET 10 WPF supports arm64. CI builds both. arm64 is marked "community-tested" until a maintainer verifies it.

### D-19 Runtime packaging: self-contained per architecture
- **Alternative:** framework-dependent (about 1 MB, but needs the .NET 10 Desktop Runtime installed first).
- **Why:** first-run success matters more than download size for a free utility. Self-contained, compressed single-file (WPF can't be trimmed) is about 30–40 MB zipped. winget can additionally offer the framework-dependent build with a runtime dependency declared.
- .NET 10 is an LTS release (supported to Nov 2028), which suits a hobby-paced OSS project.
