#!/usr/bin/env bash
# Build all CRC-32 projects and collect artefacts into ./dist/
#
# Usage (from repo root):
#   ./build/build-all.sh
#   ./build/build-all.sh --skip-tests
#   CONFIGURATION=Debug ./build/build-all.sh
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_TESTS=0
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIST="$ROOT/dist"

for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    *) echo "Unknown argument: $arg"; exit 1 ;;
  esac
done

step()  { echo -e "\n==> $1"; }
ok()    { echo "    OK: $1"; }

# -----------------------------------------------------------------------
step "Clean dist/"
rm -rf "$DIST"
mkdir -p "$DIST"

# -----------------------------------------------------------------------
if [ "$SKIP_TESTS" -eq 0 ]; then
  step "Run tests"
  dotnet test "$ROOT/tests/Crc.Core.Tests/Crc.Core.Tests.csproj" -c "$CONFIGURATION" --nologo
  ok "All tests passed"
fi

# -----------------------------------------------------------------------
step "Pack NuGet (Crc.Core)"
dotnet pack "$ROOT/src/Crc.Core/Crc.Core.csproj" -c "$CONFIGURATION" -o "$DIST/nuget" --nologo
ok "NuGet → dist/nuget/"

# -----------------------------------------------------------------------
step "Publish CLI (win-x64)"
dotnet publish "$ROOT/src/Crc.Cli/Crc.Cli.csproj" -c "$CONFIGURATION" \
  -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o "$DIST/cli/win-x64" --nologo
ok "CLI win-x64 → dist/cli/win-x64/"

step "Publish CLI (linux-x64)"
dotnet publish "$ROOT/src/Crc.Cli/Crc.Cli.csproj" -c "$CONFIGURATION" \
  -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o "$DIST/cli/linux-x64" --nologo
ok "CLI linux-x64 → dist/cli/linux-x64/"

# -----------------------------------------------------------------------
step "Publish API"
dotnet publish "$ROOT/src/Crc.Api/Crc.Api.csproj" -c "$CONFIGURATION" \
  -o "$DIST/api" --nologo
ok "API → dist/api/"

# -----------------------------------------------------------------------
step "Publish Desktop (win-x64)"
dotnet publish "$ROOT/src/Crc.Desktop/Crc.Desktop.csproj" -c "$CONFIGURATION" \
  -r win-x64 --self-contained true \
  -o "$DIST/desktop/win-x64" --nologo
ok "Desktop win-x64 → dist/desktop/win-x64/"

step "Publish Desktop (linux-x64)"
dotnet publish "$ROOT/src/Crc.Desktop/Crc.Desktop.csproj" -c "$CONFIGURATION" \
  -r linux-x64 --self-contained true \
  -o "$DIST/desktop/linux-x64" --nologo
ok "Desktop linux-x64 → dist/desktop/linux-x64/"

# -----------------------------------------------------------------------
step "Done"
echo ""
echo "Artefacts in: $DIST"
find "$DIST" -type f | sort
