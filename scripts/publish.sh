#!/bin/zsh
# Publish Stash for Windows as single self-contained exes for ARM64 and x64 into dist/.
# Works on macOS (EnableWindowsTargeting) and on Windows. Usage: scripts/publish.sh [arm64|x64|all]
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET=${DOTNET:-$([ -x ~/.dotnet9/dotnet ] && echo ~/.dotnet9/dotnet || command -v dotnet)}
VER=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' src/Stash/Stash.csproj)
which=${1:-arm64}
[ "$which" = all ] && archs=(arm64 x64) || archs=($which)
for arch in "${archs[@]}"; do
  rid=win-$arch
  "$DOTNET" publish src/Stash/Stash.csproj -c Release -r $rid --nologo -v q -o dist/$rid
  "$DOTNET" publish src/Stash/Stash.csproj -c Release -r $rid --nologo -v q -o dist/$rid-cli -p:CliBuild=true
  cp "dist/$rid/Stash for Windows.exe" "dist/Stash-for-Windows-$VER-$arch.exe"
  cp dist/$rid-cli/stash.exe "dist/stash-$VER-$arch.exe"
  ls -lh "dist/Stash-for-Windows-$VER-$arch.exe" "dist/stash-$VER-$arch.exe"
done
