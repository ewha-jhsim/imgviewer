#!/usr/bin/env bash
# Cross-build the Windows ImgViewer from Linux/macOS.
#
# Usage:
#   ./build.sh                  # folder publish: self-contained, NO single-file extraction (most reliable)
#   ./build.sh single           # one portable .exe (self-contained, single-file) — convenient but some AV
#                               #   setups block its temp self-extraction (app may appear to "do nothing")
#   ./build.sh framework        # tiny exe, needs the .NET 8 Desktop Runtime installed on the target
#
# Requires the OFFICIAL Microsoft .NET 8 SDK (the one that ships the WindowsDesktop SDK).
# The Ubuntu/Debian "dotnet-sdk-8.0" package does NOT include it. Install via:
#   wget https://dot.net/v1/dotnet-install.sh && bash dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet-ms"
set -euo pipefail

MODE="${1:-folder}"
RID="win-x64"

# Prefer a local Microsoft SDK install if present (has the WindowsDesktop SDK).
if [ -x "$HOME/.dotnet-ms/dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet-ms"
  export PATH="$HOME/.dotnet-ms:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "Using dotnet: $(command -v dotnet)  ($(dotnet --version))"

case "$MODE" in
  single)
    OUT="publish/single"
    echo "Building self-contained single-file exe -> $OUT"
    dotnet publish -c Release -r "$RID" --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -o "$OUT"
    ls -lh "$OUT"/ImgViewer.exe
    ;;
  framework)
    OUT="publish/framework"
    echo "Building framework-dependent exe -> $OUT"
    dotnet publish -c Release -r "$RID" --self-contained false -o "$OUT"
    echo "Done -> $OUT (needs .NET 8 Desktop Runtime on the target)"
    ;;
  folder|*)
    OUT="publish/win-x64"
    echo "Building self-contained folder publish -> $OUT"
    dotnet publish -c Release -r "$RID" --self-contained true \
      -p:PublishSingleFile=false \
      -o "$OUT"
    echo "Done -> $OUT/ImgViewer.exe  (copy the WHOLE folder to the target, or build an installer)"
    ;;
esac
