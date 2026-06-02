#!/usr/bin/env bash
#
# install.sh — bootstrap de Aethra en una máquina nueva.
#
# Qué hace:
#   1. Verifica prerequisitos (dotnet 10, node 24+, psql 16, opcionalmente docker/podman).
#   2. Crea la BD `aethra` si no existe.
#   3. Restaura y compila la solución.
#   4. Pide (o lee desde env) el AdminEmail/AdminPasswordSeed.
#   5. Escribe `apps/api/appsettings.Local.json` con los valores que pediste.
#   6. Aplica migraciones EF (arranca la API una vez en modo Development — las aplica solas).
#   7. Instala deps del frontend.
#   8. Imprime los comandos para arrancar central + web + satélite (no los arranca solo:
#      eso queda en tu control para que el shell se libere).
#
# Idempotente: ejecutar dos veces no rompe nada (la BD se reutiliza, los appsettings
# locales se respetan si ya existen y son distintos del default).
#
# Uso:
#   ./install.sh                                       # interactivo
#   AETHRA_ADMIN_EMAIL=tu@correo.com \
#   AETHRA_ADMIN_PASSWORD=clave-segura \
#   AETHRA_DB_HOST=localhost AETHRA_DB_USER=postgres \
#   ./install.sh                                       # no-interactivo
#
# Variables opcionales:
#   AETHRA_DB_HOST         (default: localhost)
#   AETHRA_DB_PORT         (default: 5432)
#   AETHRA_DB_NAME         (default: aethra)
#   AETHRA_DB_USER         (default: aethra)
#   AETHRA_DB_PASSWORD     (default: changeme)
#   AETHRA_DB_ADMIN_USER   (default: postgres — usado solo para crear la BD)
#   AETHRA_ADMIN_EMAIL     (interactivo si no se pasa)
#   AETHRA_ADMIN_PASSWORD  (interactivo si no se pasa)
#   AETHRA_SKIP_FRONTEND   (1 = no instalar deps de apps/web)

set -e

# Colores opcionales — desactivados si la salida no es a un TTY o NO_COLOR está set.
if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
  C_BOLD=$'\e[1m'; C_RED=$'\e[31m'; C_GREEN=$'\e[32m'; C_YELLOW=$'\e[33m'; C_CYAN=$'\e[36m'; C_RESET=$'\e[0m'
else
  C_BOLD=""; C_RED=""; C_GREEN=""; C_YELLOW=""; C_CYAN=""; C_RESET=""
fi

step() { echo; echo "${C_BOLD}${C_CYAN}=>${C_RESET} ${C_BOLD}$*${C_RESET}"; }
ok()   { echo "  ${C_GREEN}✓${C_RESET} $*"; }
warn() { echo "  ${C_YELLOW}!${C_RESET} $*"; }
die()  { echo "${C_RED}✗${C_RESET} $*" >&2; exit 1; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

# ─────────────────────────────────────────────────────────────────────────────
# 1. Prerequisitos
# ─────────────────────────────────────────────────────────────────────────────
step "Verificando prerequisitos"

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "$1 no encontrado en PATH. Instalalo y vuelve a correr."
}

require_cmd dotnet
require_cmd psql
require_cmd node
require_cmd npm

DOTNET_VERSION="$(dotnet --version 2>/dev/null || echo 0)"
DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
[[ "$DOTNET_MAJOR" -ge 10 ]] || die ".NET 10 SDK requerido (instalado: $DOTNET_VERSION)."
ok ".NET $DOTNET_VERSION"

NODE_VERSION="$(node --version 2>/dev/null | sed 's/^v//')"
NODE_MAJOR="${NODE_VERSION%%.*}"
[[ "$NODE_MAJOR" -ge 24 ]] || die "Node.js 24+ requerido (instalado: $NODE_VERSION)."
ok "Node.js $NODE_VERSION"

PSQL_VERSION="$(psql --version | awk '{print $3}' | cut -d. -f1)"
[[ "$PSQL_VERSION" -ge 16 ]] || warn "psql $PSQL_VERSION detectado (recomendado 16+)."
ok "psql $PSQL_VERSION"

# Container runtime es opcional para la build/arranque del central, pero requerido para
# usar el satélite con builds reales. Lo reportamos sin fallar.
if command -v docker >/dev/null 2>&1; then
  ok "docker $(docker --version 2>/dev/null | awk '{print $3}' | tr -d ',')"
elif command -v podman >/dev/null 2>&1; then
  ok "podman $(podman --version 2>/dev/null | awk '{print $3}')"
else
  warn "Ni docker ni podman en PATH. El central arrancará igual, pero el satélite no podrá ejecutar builds reales."
fi

# Nixpacks es opcional — solo se usa cuando un Template tiene BuildType=Nixpacks (F11.2).
# Si falta, los builds Nixpacks devuelven 'nixpacks_not_installed' con instrucciones de instalación.
if command -v nixpacks >/dev/null 2>&1; then
  ok "nixpacks $(nixpacks --version 2>/dev/null | head -n1)"
else
  warn "nixpacks no encontrado. Builds con BuildType=Nixpacks fallarán hasta instalarlo en el satélite:"
  warn "  curl -fsSL https://nixpacks.com/install.sh | bash"
fi

# ─────────────────────────────────────────────────────────────────────────────
# 2. Configuración (env o interactivo)
# ─────────────────────────────────────────────────────────────────────────────
step "Configurando credenciales"

DB_HOST="${AETHRA_DB_HOST:-localhost}"
DB_PORT="${AETHRA_DB_PORT:-5432}"
DB_NAME="${AETHRA_DB_NAME:-aethra}"
DB_USER="${AETHRA_DB_USER:-aethra}"
DB_PASSWORD="${AETHRA_DB_PASSWORD:-changeme}"
DB_ADMIN_USER="${AETHRA_DB_ADMIN_USER:-postgres}"

if [[ -z "${AETHRA_ADMIN_EMAIL:-}" ]]; then
  read -r -p "Email del admin (default admin@aethra.local): " ADMIN_EMAIL
  ADMIN_EMAIL="${ADMIN_EMAIL:-admin@aethra.local}"
else
  ADMIN_EMAIL="$AETHRA_ADMIN_EMAIL"
fi

if [[ -z "${AETHRA_ADMIN_PASSWORD:-}" ]]; then
  read -r -s -p "Password del admin (input oculto): " ADMIN_PASSWORD
  echo
  [[ -n "$ADMIN_PASSWORD" ]] || die "Password vacía no permitida."
else
  ADMIN_PASSWORD="$AETHRA_ADMIN_PASSWORD"
fi

ok "Admin: $ADMIN_EMAIL"
ok "BD: $DB_USER@$DB_HOST:$DB_PORT/$DB_NAME"

# ─────────────────────────────────────────────────────────────────────────────
# 3. BD
# ─────────────────────────────────────────────────────────────────────────────
step "Creando BD '$DB_NAME' si no existe"

# Detecta si el rol y la BD ya existen para no fallar.
DB_EXISTS="$(PGPASSWORD="${PGPASSWORD:-}" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_ADMIN_USER" -tAc \
  "SELECT 1 FROM pg_database WHERE datname='$DB_NAME'" 2>/dev/null || echo "")"

ROLE_EXISTS="$(PGPASSWORD="${PGPASSWORD:-}" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_ADMIN_USER" -tAc \
  "SELECT 1 FROM pg_roles WHERE rolname='$DB_USER'" 2>/dev/null || echo "")"

if [[ "$ROLE_EXISTS" != "1" ]]; then
  PGPASSWORD="${PGPASSWORD:-}" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_ADMIN_USER" -d postgres \
    -c "CREATE ROLE \"$DB_USER\" WITH LOGIN PASSWORD '$DB_PASSWORD';" >/dev/null
  ok "Rol '$DB_USER' creado"
else
  ok "Rol '$DB_USER' ya existe"
fi

if [[ "$DB_EXISTS" != "1" ]]; then
  PGPASSWORD="${PGPASSWORD:-}" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_ADMIN_USER" -d postgres \
    -c "CREATE DATABASE \"$DB_NAME\" OWNER \"$DB_USER\";" >/dev/null
  ok "BD '$DB_NAME' creada"
else
  ok "BD '$DB_NAME' ya existe (se reutiliza)"
fi

# ─────────────────────────────────────────────────────────────────────────────
# 4. Build de la solución
# ─────────────────────────────────────────────────────────────────────────────
step "Restaurando y compilando solución"
dotnet build Aethra.slnx -c Debug --nologo --verbosity minimal
ok "Build limpio"

# ─────────────────────────────────────────────────────────────────────────────
# 5. appsettings.Local.json
# ─────────────────────────────────────────────────────────────────────────────
step "Generando apps/api/appsettings.Local.json"

LOCAL_JSON="apps/api/appsettings.Local.json"
CONN_STRING="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD;Include Error Detail=true"

# Escapa comillas dobles en password para JSON (raro pero posible).
JSON_PASSWORD_ESCAPED="$(printf '%s' "$ADMIN_PASSWORD" | sed 's/\\/\\\\/g; s/"/\\"/g')"
JSON_EMAIL_ESCAPED="$(printf '%s' "$ADMIN_EMAIL" | sed 's/\\/\\\\/g; s/"/\\"/g')"
JSON_CONN_ESCAPED="$(printf '%s' "$CONN_STRING" | sed 's/\\/\\\\/g; s/"/\\"/g')"

cat > "$LOCAL_JSON" <<EOF
{
  "ConnectionStrings": {
    "Aethra": "$JSON_CONN_ESCAPED"
  },
  "Identity": {
    "AdminEmail": "$JSON_EMAIL_ESCAPED",
    "AdminPasswordSeed": "$JSON_PASSWORD_ESCAPED"
  },
  "Tls": {
    "AccountEmail": "$JSON_EMAIL_ESCAPED",
    "UseStaging": true,
    "RenewBeforeDays": 30
  }
}
EOF

ok "Escrito $LOCAL_JSON (no commitear — ya está en .gitignore vía appsettings.Local.json)"

# Asegura que esté en .gitignore (idempotente).
if ! grep -qxF "apps/api/appsettings.Local.json" .gitignore 2>/dev/null; then
  echo "apps/api/appsettings.Local.json" >> .gitignore
  ok "Añadido a .gitignore"
fi

# ─────────────────────────────────────────────────────────────────────────────
# 6. Aplicar migraciones (arrancando la API brevemente y matándola al detectar el listen)
# ─────────────────────────────────────────────────────────────────────────────
step "Aplicando migraciones EF (arrancando API hasta detectar listen)"

API_LOG="$(mktemp)"
( cd apps/api && ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile ) \
  > "$API_LOG" 2>&1 &
API_PID=$!

# Espera hasta 90s a que la API levante o muera.
for _ in $(seq 1 90); do
  if grep -q "Now listening on" "$API_LOG" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    cat "$API_LOG" | tail -30
    rm -f "$API_LOG"
    die "API murió antes de aplicar migraciones — ver log arriba."
  fi
  sleep 1
done

if grep -q "Now listening on" "$API_LOG"; then
  ok "API listening — migraciones aplicadas"
else
  warn "Timeout esperando 'Now listening on' — log:"
  tail -30 "$API_LOG"
fi

kill "$API_PID" 2>/dev/null || true
wait "$API_PID" 2>/dev/null || true
rm -f "$API_LOG"

# ─────────────────────────────────────────────────────────────────────────────
# 7. Frontend deps
# ─────────────────────────────────────────────────────────────────────────────
if [[ "${AETHRA_SKIP_FRONTEND:-0}" == "1" ]]; then
  warn "AETHRA_SKIP_FRONTEND=1 — saltando npm install"
else
  step "Instalando deps de apps/web"
  ( cd apps/web && npm install --no-audit --no-fund --loglevel=error )
  ok "Frontend listo"
fi

# ─────────────────────────────────────────────────────────────────────────────
# 8. Fin
# ─────────────────────────────────────────────────────────────────────────────
cat <<EOF

${C_BOLD}${C_GREEN}✓ Aethra instalado.${C_RESET}

${C_BOLD}Para arrancar:${C_RESET}

  # Central API (puerto 5000)
  dotnet run --project apps/api

  # Frontend (puerto 3000)
  cd apps/web && npm run dev

  # (Opcional) satélite local apuntando al central
  dotnet run --project apps/satellite

${C_BOLD}Login:${C_RESET} $ADMIN_EMAIL / (la password que diste)
${C_BOLD}UI:${C_RESET}    http://localhost:3000
${C_BOLD}API:${C_RESET}   http://localhost:5000  (docs: /openapi/v1.json)
${C_BOLD}MCP:${C_RESET}   ws://localhost:5000/mcp

EOF
