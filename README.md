# Sway

**Edge-swipe volume and brightness for Windows 11 Precision Touchpads.**

Slide one finger up or down along the **right edge** of your touchpad to change the volume, or along the **left edge** to change screen brightness. It works like the edge sliders on a phone: the level follows your finger, and a swipe over the full height of the touchpad covers the full range. The middle of the touchpad works exactly as before.

```
┌────────┬──────────────────────────────┬────────┐
│   ☀    │                              │   🔊   │
│ BRIGHT │      normal touchpad         │ VOLUME │
│  ↕     │                              │   ↕    │
└────────┴──────────────────────────────┴────────┘
```

- No admin rights, no driver, nothing installed system-wide.
- **No network connections and no telemetry. Ever.** ([why that matters](#privacy))
- Free and open source (MIT).

> **Status: pre-alpha (0.1).** It works on the maintainer's laptop. Hardware reports from other touchpads are very welcome; see [docs/HARDWARE.md](docs/HARDWARE.md).

## Requirements

- Windows 11 (Windows 10 may work but isn't tested)
- A **Windows Precision Touchpad**. Almost every laptop from 2018 onwards has one. To check: **Settings → Bluetooth & devices → Touchpad**. If you see "Your PC has a precision touchpad", you're set.
- Brightness control works for the **built-in screen** only, not external monitors.

## Install

1. Download `Sway-<version>-win-x64.zip` (or `win-arm64` for Snapdragon laptops) from [Releases](https://github.com/ayushgade06/sway/releases).
2. Extract it anywhere and double-click **Sway.exe**. Sway appears in the system tray (you may need to click the **^** arrow to see it).
3. Optional: to install it properly (Start menu entry, listed in **Settings → Apps** for uninstalling), open a terminal in the extracted folder and run:
   ```powershell
   .\Sway.exe --install
   ```
   This copies Sway to `%LOCALAPPDATA%\Programs\Sway` for your user only. It needs no admin rights.

### "Windows protected your PC"

Sway isn't code-signed yet (signing costs money, and we're applying for free open-source signing), so Windows SmartScreen warns about it. Click **More info → Run anyway**. To verify that your download is exactly what our build server produced, compare its checksum with the `.sha256` file published next to it:

```powershell
Get-FileHash .\Sway-0.1.0-win-x64.zip -Algorithm SHA256
```

Some antivirus products dislike apps that read raw input and use a mouse hook (see [what Sway does with your input](#what-sway-does-with-your-input)). If yours flags Sway, please open an issue.

## Use it

- **Right edge:** swipe up for louder, down for quieter. Raising the volume also unmutes.
- **Left edge:** swipe up for brighter, down for dimmer.
- Start the swipe **inside** the edge strip (about a finger-width). A stroke that starts in the middle and slides to the edge never changes anything, so normal mousing is safe.
- Taps, two-finger scrolling and three- or four-finger gestures are never affected.
- A small indicator shows the level at the top of the screen and fades out when you lift your finger.

**Tray menu** (right-click the icon): *Enabled*, *Settings…*, *Quit*. In **Settings** you can swap the edges, turn either edge off, change the edge width, change how long a swipe goes from 0 to 100 (as a percentage of the touchpad height), keep the cursor still during swipes, and start Sway when you sign in. Changes apply immediately.

## Privacy

Sway sees every touchpad movement, so trust is the whole product:

- It makes **zero network connections**. There is no update check, no analytics and no crash reporting. A test in CI fails if any Sway assembly references a networking library.
- Touch data never leaves the process and is never written to disk. The only exception is when **you** run `Sway.Probe record`.
- Settings live in `%APPDATA%\Sway\settings.json`. A small log (no touch coordinates) lives in `%LOCALAPPDATA%\Sway\log.txt`.

## What Sway does with your input

Antivirus reviewers and curious users can check all of this in the source:

- **Raw Input** (`src/Sway.Core/Input/RawInputTouchSource.cs`): Sway subscribes to a *copy* of the touchpad's reports (HID usage 0x0D/0x05). It can't change or block what the touchpad does.
- **Low-level mouse hook** (`src/Sway.Core/Input/CursorFreezer.cs`), only when *Keep the cursor still* is on: while an edge swipe is in progress, Sway drops the cursor movement that the same finger produces, then lets everything through again when you lift. If Sway stops receiving touch frames, the freeze releases itself within 250 ms. If Sway hangs or exits, Windows removes the hook automatically.
- **Volume** through the Windows Core Audio API, and **brightness** through WMI (`WmiMonitorBrightnessMethods`), the same mechanism Windows' own brightness slider uses.

## Uninstall

- If you used `--install`: **Settings → Apps → Installed apps → Sway → Uninstall**. Sway asks whether to delete your settings too.
- If you just ran the zip: choose **Quit** from the tray menu and delete the folder. Settings are in `%APPDATA%\Sway`. If you enabled "start when I sign in", turn it off first in Settings.

## Troubleshooting and hardware reports

The release includes **Sway.Probe.exe**, a diagnostic tool:

```powershell
.\Sway.Probe.exe report     # paste-ready hardware report; contains no personal data
.\Sway.Probe.exe live       # watch your fingers on a map of the touchpad
.\Sway.Probe.exe gestures   # see what Sway's recognizer detects
```

Please post `report` output in a **Hardware report** issue, whether Sway works for you or not.

**Known limitations**
- Exclusive full-screen games cover the indicator. The level still changes.
- Brightness may be unavailable in some discrete-GPU (MUX) display modes. The brightness edge then turns off and the tray icon says so.
- Adaptive or content-adaptive brightness can override changes you make.

## Build from source

```powershell
git clone https://github.com/ayushgade06/sway; cd sway
dotnet test                              # needs the .NET 10 SDK; no touchpad needed
dotnet run --project src/Sway            # the tray app
./eng/publish.ps1 -Arch x64              # self-contained zip in dist/
```

See [CONTRIBUTING.md](CONTRIBUTING.md). Design docs are in [docs/](docs).

## License

[MIT](LICENSE). Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
