# Security policy

Sway reads raw touchpad input and can install a low-level mouse hook, so we take reports seriously.

## Reporting a vulnerability

Please **do not** open a public issue. Use GitHub's private vulnerability reporting:
**Security** tab → **Report a vulnerability** on this repository.

We aim to reply within 7 days. Supported version: the latest release.

## What Sway does with your input

- It reads a copy of touchpad reports through Raw Input. It never modifies the touchpad.
- If cursor freezing is enabled, a `WH_MOUSE_LL` hook drops cursor moves **only while an edge gesture is active**.
- It makes no network connections and writes no touch data to disk (except when you explicitly run `Sway.Probe record`).
