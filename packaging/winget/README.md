# winget manifest (prepared, not submitted)

After a GitHub Release exists for the version:

1. Put the SHA-256 values from `Sway-<ver>-win-<arch>.zip.sha256` into `SwayContributors.Sway.installer.yaml`.
2. Validate: `winget validate --manifest packaging/winget/manifests/s/SwayContributors/Sway/<ver>`
3. Test locally: `winget install --manifest packaging/winget/manifests/s/SwayContributors/Sway/<ver>`
4. Submit a PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) (maintainer decision; see DECISIONS D-14).
