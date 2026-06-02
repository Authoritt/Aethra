#!/usr/bin/env bash
# Publica el binario del satélite Aethra para linux-x64 y linux-arm64 como tarballs
# en apps/api/satellite-binaries/. El endpoint público GET /api/satellite/binary?arch=...
# del central los sirve a los scripts de install (auto SSH + manual one-liner).
#
# Uso (desde la raíz del repo):
#   bash scripts/publish-satellite.sh
#
# Requisitos:
#   - .NET 10 SDK
#   - En Linux/macOS: ningún paquete extra. En Windows el cross-publish a linux-* funciona
#     siempre que dotnet sepa el runtime — si no, corré esto desde una CI Linux.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="$REPO_ROOT/apps/api/satellite-binaries"
BUILD_DIR="$REPO_ROOT/build/satellite"

mkdir -p "$OUT_DIR"

for ARCH in linux-x64 linux-arm64; do
  echo "==> Publicando satélite para $ARCH..."
  TARGET="$BUILD_DIR/$ARCH"
  rm -rf "$TARGET"
  dotnet publish "$REPO_ROOT/apps/satellite/Aethra.Satellite.csproj" \
    -c Release \
    -r "$ARCH" \
    -p:PublishTrimmed=false \
    -p:SelfContained=true \
    -p:DebugType=embedded \
    -o "$TARGET/"

  TARBALL="$OUT_DIR/aethra-satellite-$ARCH.tar.gz"
  tar -czf "$TARBALL" -C "$TARGET" .
  SIZE=$(du -h "$TARBALL" | cut -f1)
  echo "    -> $TARBALL  ($SIZE)"
done

echo ""
echo "Listo. Tarballs en $OUT_DIR (no se commitean — están en .gitignore)."
