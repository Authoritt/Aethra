#!/usr/bin/env bash
# Aethra satellite installer — standalone.
#
# Uso (curl one-liner):
#   curl -fsSL https://aethra.example.com/install-satellite.sh | \
#     sudo bash -s -- \
#       --central-url https://aethra.example.com \
#       --token sat_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx \
#       --runtime docker
#
# Flags:
#   --central-url <url>      URL pública del central Aethra (requerido).
#   --token <plaintext>      Token de satélite emitido por POST /api/vms (requerido).
#   --runtime <docker|podman> Runtime de contenedores que usará el satélite. Default docker.
#   --install-runtime         Instala el runtime si no está presente (apt-get o dnf).
#
# Idempotente: si /opt/aethra-satellite ya existe, lo reescribe. Si systemd ya tiene el
# servicio, lo reinicia. NO borra credenciales viejas.
set -euo pipefail

CENTRAL_URL=""
TOKEN=""
RUNTIME="docker"
INSTALL_RUNTIME=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --central-url)
      CENTRAL_URL="$2"
      shift 2
      ;;
    --token)
      TOKEN="$2"
      shift 2
      ;;
    --runtime)
      RUNTIME="$2"
      shift 2
      ;;
    --install-runtime)
      INSTALL_RUNTIME=1
      shift
      ;;
    -h|--help)
      sed -n '/^# Uso/,/^set -euo/p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Flag no reconocida: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$CENTRAL_URL" ]]; then
  echo "Falta --central-url" >&2
  exit 1
fi
if [[ -z "$TOKEN" ]]; then
  echo "Falta --token" >&2
  exit 1
fi
if [[ "$RUNTIME" != "docker" && "$RUNTIME" != "podman" ]]; then
  echo "--runtime debe ser 'docker' o 'podman'" >&2
  exit 1
fi

# Detectar arch
ARCH="$(uname -m)"
case "$ARCH" in
  x86_64) BINARCH="linux-x64" ;;
  aarch64) BINARCH="linux-arm64" ;;
  *) echo "Arquitectura no soportada: $ARCH" >&2; exit 1 ;;
esac

echo "==> Aethra satellite installer"
echo "    central:  $CENTRAL_URL"
echo "    arch:     $ARCH -> $BINARCH"
echo "    runtime:  $RUNTIME (install=$INSTALL_RUNTIME)"

# Instalar container runtime si flag y falta
if [[ "$INSTALL_RUNTIME" == "1" ]]; then
  if ! command -v "$RUNTIME" >/dev/null 2>&1; then
    echo "==> $RUNTIME no encontrado. Instalando..."
    if command -v apt-get >/dev/null 2>&1; then
      PKG="$RUNTIME"
      if [[ "$RUNTIME" == "docker" ]]; then
        PKG="docker.io"
      fi
      sudo apt-get update
      sudo DEBIAN_FRONTEND=noninteractive apt-get install -y "$PKG"
    elif command -v dnf >/dev/null 2>&1; then
      sudo dnf install -y "$RUNTIME"
    else
      echo "No detecto apt-get ni dnf. Instala $RUNTIME manualmente y volvé a correr." >&2
      exit 1
    fi
    sudo systemctl enable --now "$RUNTIME"
  else
    echo "==> $RUNTIME ya está instalado, skip."
  fi
fi

# Descargar binario
DOWNLOAD_URL="${CENTRAL_URL%/}/api/satellite/binary?arch=${BINARCH}"
echo "==> Descargando satélite de $DOWNLOAD_URL"
curl -fL -o /tmp/aethra-sat.tar.gz "$DOWNLOAD_URL"

# Extraer + permisos
echo "==> Extrayendo en /opt/aethra-satellite"
sudo mkdir -p /opt/aethra-satellite
sudo tar -xzf /tmp/aethra-sat.tar.gz -C /opt/aethra-satellite
sudo chmod +x /opt/aethra-satellite/Aethra.Satellite

# Systemd unit
echo "==> Escribiendo /etc/systemd/system/aethra-satellite.service"
sudo tee /etc/systemd/system/aethra-satellite.service > /dev/null <<EOF
[Unit]
Description=Aethra Satellite
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/opt/aethra-satellite/Aethra.Satellite
Environment=AETHRA_CENTRAL_URL=${CENTRAL_URL}
Environment=AETHRA_SATELLITE_TOKEN=${TOKEN}
Environment=Satellite__ContainerRuntime=${RUNTIME}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

# Arrancar
echo "==> systemctl daemon-reload && enable --now aethra-satellite"
sudo systemctl daemon-reload
sudo systemctl enable --now aethra-satellite

echo ""
echo "Aethra satellite instalado y corriendo."
echo "Logs: journalctl -u aethra-satellite -f"
echo "Estado: systemctl status aethra-satellite"
