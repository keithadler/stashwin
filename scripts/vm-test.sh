#!/bin/zsh
# Copy the ARM64 exes into the Windows VM and run the self-test there.
set -euo pipefail
cd "$(dirname "$0")/.."
VM=~/Downloads/win11arm/vmssh.sh
VER=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' src/Stash/Stash.csproj)
$VM ssh 'Stop-Process -Name "Stash for Windows",stash -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force C:\Stash | Out-Null'
$VM scp "dist/Stash-for-Windows-$VER-arm64.exe" 'keith@VM:C:/Stash/Stash for Windows.exe'
$VM scp "dist/stash-$VER-arm64.exe" 'keith@VM:C:/Stash/stash.exe'
$VM scp tests/fixtures 'keith@VM:C:/Stash/fixtures'
$VM ssh '$env:STASH_FIXTURES="C:\Stash\fixtures"; C:\Stash\stash.exe selftest | Select-Object -Last 1; C:\Stash\stash.exe version'
