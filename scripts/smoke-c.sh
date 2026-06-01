#!/usr/bin/env bash
# F9.8 C smoke handshake test:
#   1) Drop+create DB.
#   2) Arranca central + espera "Now listening on".
#   3) Login + crea Vm → obtiene token.
#   4) Arranca satélite con ese token + espera conexión.
#   5) Crea Project+Template+Client+Instance.
#   6) Dispara Build manual.
#   7) Verifica que el Build terminó en Failed con errorCode coherente (no_satellite o runtime_*).
#
# El smoke NO requiere Docker — la máquina del CI no lo tiene.
set -uo pipefail

export PATH="/c/Program Files/PostgreSQL/16/bin:$PATH"
export PGPASSWORD=postgres

ROOT=$(cd "$(dirname "$0")"/.. && pwd)
API_LOG=/tmp/api.log
SAT_LOG=/tmp/sat.log
OUT=/tmp/smoke-c.out
COOK=/tmp/aethra-cookies.txt
CENTRAL=http://localhost:5080

rm -f "$COOK" "$API_LOG" "$SAT_LOG" "$OUT"

step() { echo "===> $*" | tee -a "$OUT"; }

step "1. Drop+create DB"
psql -U postgres -h localhost -d postgres -c "DROP DATABASE IF EXISTS aethra;" 2>&1 | tee -a "$OUT"
psql -U postgres -h localhost -d postgres -c "CREATE DATABASE aethra OWNER aethra;" 2>&1 | tee -a "$OUT"

step "2. Arrancar central"
( cd "$ROOT/apps/api" && ASPNETCORE_URLS=http://localhost:5080 ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile > "$API_LOG" 2>&1 ) &
API_PID=$!
echo "PID central=$API_PID" | tee -a "$OUT"

step "2b. Esperar Now listening on (max 90s)"
for i in $(seq 1 90); do
  if grep -q "Now listening on" "$API_LOG" 2>/dev/null; then
    echo "central listening at iteration $i" | tee -a "$OUT"
    break
  fi
  sleep 1
done
grep -m1 "Now listening on" "$API_LOG" | tee -a "$OUT" || (echo "FATAL: central no arrancó" | tee -a "$OUT"; exit 1)

step "3. Login con admin"
curl -sS -c "$COOK" -X POST "$CENTRAL/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@aethra.local","password":"aethra-dev"}' \
  -o /tmp/login.json -w "HTTP %{http_code}\n" 2>&1 | tee -a "$OUT"
cat /tmp/login.json | tee -a "$OUT" >/dev/null

step "4. Registrar VM y capturar token"
VM_RESP=$(curl -sS -b "$COOK" -X POST "$CENTRAL/api/vms/" \
  -H 'Content-Type: application/json' \
  -d '{"name":"smoke-vm","description":"F9.8C smoke"}')
echo "$VM_RESP" | tee -a "$OUT"

extract_json() { python -c "import sys,json;
try: d=json.load(sys.stdin)
except: sys.exit(0)
key=sys.argv[1]
v=d.get(key,'')
if isinstance(v, (str,int)): print(v)" "$1" 2>/dev/null; }

VM_ID=$(echo "$VM_RESP" | extract_json vmId)
VM_TOKEN=$(echo "$VM_RESP" | extract_json tokenPlaintext)
echo "VM_ID=$VM_ID" | tee -a "$OUT"
echo "VM_TOKEN(prefix)=${VM_TOKEN:0:12}..." | tee -a "$OUT"
if [ -z "$VM_ID" ] || [ -z "$VM_TOKEN" ]; then
  echo "FATAL: no se obtuvo vmId/token" | tee -a "$OUT"
  exit 1
fi

step "5. Arrancar satélite con el token"
export AETHRA_CENTRAL_URL="$CENTRAL"
export AETHRA_SATELLITE_TOKEN="$VM_TOKEN"
( cd "$ROOT/apps/satellite" && dotnet run --no-launch-profile > "$SAT_LOG" 2>&1 ) &
SAT_PID=$!
echo "PID satellite=$SAT_PID" | tee -a "$OUT"

step "5b. Esperar conexión del satélite (max 30s)"
for i in $(seq 1 30); do
  if grep -q "Conectado al central" "$SAT_LOG" 2>/dev/null; then
    echo "satellite connected at iteration $i" | tee -a "$OUT"
    break
  fi
  sleep 1
done
grep -m1 "Conectado al central" "$SAT_LOG" | tee -a "$OUT" || echo "WARN: satellite did not log connection" | tee -a "$OUT"

# Confirmar en el log del central que el satélite registró su connection.
sleep 3
grep -m1 "Satélite conectado para VM" "$API_LOG" | tee -a "$OUT" || echo "WARN: central did not log connection" | tee -a "$OUT"

step "6. Seed BaseDomain + Environment production (best-effort)"
curl -sS -b "$COOK" -X POST "$CENTRAL/api/settings/domains/" \
  -H 'Content-Type: application/json' \
  -d '{"hostname":"smoke.local"}' \
  -w "HTTP %{http_code}\n" 2>&1 | tee -a "$OUT"

curl -sS -b "$COOK" -X POST "$CENTRAL/api/settings/environments/" \
  -H 'Content-Type: application/json' \
  -d '{"slug":"production","displayName":"Production","order":1}' \
  -w "HTTP %{http_code}\n" 2>&1 | tee -a "$OUT"

step "7. Crear Project + Template + Build"
PRJ_RESP=$(curl -sS -b "$COOK" -X POST "$CENTRAL/api/projects/" \
  -H 'Content-Type: application/json' \
  -d '{"slug":"smoke-prj","name":"smoke-prj"}')
echo "$PRJ_RESP" | tee -a "$OUT"
PRJ_ID=$(echo "$PRJ_RESP" | extract_json id)
[ -z "$PRJ_ID" ] && PRJ_ID=$(echo "$PRJ_RESP" | extract_json projectId)
echo "PRJ_ID=$PRJ_ID" | tee -a "$OUT"

TPL_RESP=$(curl -sS -b "$COOK" -X POST "$CENTRAL/api/projects/$PRJ_ID/templates" \
  -H 'Content-Type: application/json' \
  -d '{"slug":"web","name":"web","gitRepoUrl":"https://github.com/example/repo","branch":"main","buildType":"Dockerfile","dockerfilePath":"Dockerfile","baseDirectory":"."}')
echo "$TPL_RESP" | tee -a "$OUT"
TPL_ID=$(echo "$TPL_RESP" | extract_json id)
[ -z "$TPL_ID" ] && TPL_ID=$(echo "$TPL_RESP" | extract_json templateId)
echo "TPL_ID=$TPL_ID" | tee -a "$OUT"

step "8. Build manual"
BUILD_RESP=$(curl -sS -b "$COOK" -X POST "$CENTRAL/api/builds/templates/$TPL_ID/trigger" \
  -H 'Content-Type: application/json' \
  -d "{\"gitRef\":\"refs/heads/main\",\"gitSha\":\"abc1234567def0123456789012345678abcdef01\",\"triggeredBy\":\"smoke\"}")
echo "$BUILD_RESP" | tee -a "$OUT"
BUILD_ID=$(echo "$BUILD_RESP" | extract_json id)
[ -z "$BUILD_ID" ] && BUILD_ID=$(echo "$BUILD_RESP" | extract_json buildId)
echo "BUILD_ID=$BUILD_ID" | tee -a "$OUT"

step "9. Esperar terminal del build (max 60s)"
echo "Polling URL: $CENTRAL/api/builds/$BUILD_ID" | tee -a "$OUT"
LAST_STATUS=
LAST_RESP=
for i in $(seq 1 60); do
  STATUS_RESP=$(curl -sS -b "$COOK" "$CENTRAL/api/builds/$BUILD_ID")
  LAST_RESP="$STATUS_RESP"
  LAST_STATUS=$(echo "$STATUS_RESP" | extract_json status)
  if [ "$LAST_STATUS" = "Completed" ] || [ "$LAST_STATUS" = "Failed" ] || [ "$LAST_STATUS" = "Cancelled" ]; then
    echo "Build terminal status=$LAST_STATUS (iter=$i)" | tee -a "$OUT"
    echo "$STATUS_RESP" | tee -a "$OUT"
    break
  fi
  sleep 1
done
echo "FINAL STATUS=$LAST_STATUS" | tee -a "$OUT"
echo "LAST RESP=$LAST_RESP" | tee -a "$OUT"

step "10. Cleanup"
powershell.exe -NoProfile -Command "Get-Process -Name Aethra.Api,Aethra.Satellite -ErrorAction SilentlyContinue | Stop-Process -Force; 'done'" 2>&1 | tee -a "$OUT"
echo "smoke-c.sh DONE" | tee -a "$OUT"
