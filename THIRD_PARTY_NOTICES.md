# Third-party notices

Sway contains **no copied or adapted source code** from other projects. The HID report parser is written from the Microsoft Precision Touchpad specification.

These MIT-licensed projects were read as references while designing Sway; no code was taken from them:

- [kamektx/TouchpadGestures_Advanced](https://github.com/kamektx/TouchpadGestures_Advanced)
- [emoacht/RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad)
- [ClementGre/ThreeFingerDragOnWindows](https://github.com/ClementGre/ThreeFingerDragOnWindows)

If code is ever adapted from a third party, its copyright notice and license go here (DECISIONS D-13).

## Runtime dependencies

| Package | License | Used for |
|---|---|---|
| System.Management (Microsoft) | MIT | WMI brightness control |
| .NET runtime, WPF, Windows Forms (Microsoft) | MIT | Runtime (bundled in self-contained builds) |

## Development-only dependencies

| Package | License |
|---|---|
| xUnit | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
