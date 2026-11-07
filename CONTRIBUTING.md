# Contributing to Sway

Thanks for helping. Sway is small on purpose; please keep it that way.

## Build

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build
dotnet test
dotnet run --project src/Sway            # the tray app
dotnet run --project tools/Sway.Probe    # the diagnostic tool
```

## Send a hardware report

Sway's maintainers own one laptop. Your report tells us whether Sway works on yours.

1. Download `Sway.Probe` from the latest release (or build it).
2. Run `Sway.Probe report`. It prints a Markdown block: touchpad vendor/product ID, report layout, physical size. It contains **no** username, serial number or device path. Read it before posting.
3. Open a **Hardware report** issue and paste it.

### Record a device for the regression suite

If Sway misbehaves on your touchpad, a recording lets anyone reproduce it without your hardware:

```powershell
Sway.Probe record right-edge-up.json
```

Do the gesture, press Enter, and attach the file to your issue. Recordings contain only touch coordinates in millimetres and timestamps. Accepted recordings go into `tests/data/` and run in CI on every change.

## Rules

- **No network code.** Sway makes no network calls, ever (DECISIONS D-16). A test enforces this.
- **No new dependencies** without opening an issue first. Runtime dependencies must be MIT-compatible.
- **No copied code** without its notice in `THIRD_PARTY_NOTICES.md`.
- Input logic must be testable without a touchpad: add a recording to `tests/data/` for behaviour changes.
- Don't reverse a decision in `docs/DECISIONS.md` without discussing it in an issue.
- `dotnet build` must pass with zero warnings (warnings are errors) and `dotnet test` must be green.
