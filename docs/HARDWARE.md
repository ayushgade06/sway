# Hardware compatibility

Sway needs a Windows Precision Touchpad (PTP) that exposes the HID touchpad collection (usage page 0x0D, usage 0x05). This list is built from user reports: run `Sway.Probe report` and post the output in a [Hardware report](https://github.com/ayushgade06/sway/issues/new?template=hardware-report.yml) issue.

**Status:** ✅ works · 🟡 partly works (see notes) · ❌ doesn't work · 🔬 Probe report only, app not tried

| Laptop | Touchpad | Layout | Size (mm) | Status | Notes |
|---|---|---|---|---|---|
| Acer Nitro ANV15-51 (Win 11 build 26300, x64) | PixArt PIXA3848 (VID 0x093A PID 0x3848), I²C-HID, Microsoft inbox driver 10.0.26100.9818 | parallel, 5 contacts | 123.0 × 74.8 | ✅ | Reference device. 142 Hz, 0 dropped frames. Volume and brightness verified; WMI brightness p95 8.7 ms per write in hybrid-GPU mode; discrete-GPU (MUX) mode not yet tested. |

## What the columns mean

- **Layout:** *parallel* devices send every finger in one report. *Hybrid* devices split fingers over several reports. Sway handles both, and hybrid devices are especially useful to hear about.
- **Size:** physical size from the touchpad's HID descriptor. Sway measures edge zones and swipe length from it, so swipes feel the same on any pad size.

## Not supported

- Touchpads without Precision Touchpad support (older Synaptics/Elan "mouse-mode" drivers). `Sway.Probe report` says "no PTP collection" on these.
- Touch screens and external mice.
