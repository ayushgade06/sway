# Builds self-contained single-file Sway.exe + Sway.Probe.exe for one architecture and zips them.
# Usage: ./eng/publish.ps1 -Arch x64|arm64 [-Version 0.1.0]
param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [string]$Version = '0.1.2'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root "dist/win-$Arch"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

$common = @('-c', 'Release', '-r', "win-$Arch", '--self-contained', 'true', '-o', $out,
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', "-p:Version=$Version")
foreach ($p in 'src/Sway', 'tools/Sway.Probe') {
    dotnet publish (Join-Path $root $p) @common
    if ($LASTEXITCODE) { throw "publish $p failed" }
}
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') $out

$zip = Join-Path $root "dist/Sway-$Version-win-$Arch.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii
Get-ChildItem $out | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
"$zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)  sha256 $hash"
