#!/usr/bin/env bash
# F12.3 smoke E2E: Branch-per-Instance + Preview deployments per PR.
# Pre: API en localhost:5080 + DB limpia + admin sin TOTP.
set -uo pipefail

API=http://localhost:5080
COOKIES=/tmp/cookies.txt

step() { echo; echo "==== $1 ===="; }
fail() { echo "FAIL: $1"; exit 1; }

step "1. Login"
RESP=$(curl -s -c "$COOKIES" -X POST "$API/auth/login" -H 'content-type: application/json' \
  -d '{"email":"admin@aethra.local","password":"aethra-dev"}')
echo "$RESP"
if echo "$RESP" | grep -q '"requires_totp":true'; then
  # Smoke: disable totp directly (development convenience).
  PGPASSWORD=changeme "/c/Program Files/PostgreSQL/16/bin/psql.exe" -U aethra -d aethra -h localhost -p 5432 \
    -c "UPDATE identity.users SET totp_enabled = false, totp_secret_cipher = NULL, totp_recovery_codes_cipher = NULL, totp_recovery_codes_used_mask = 0 WHERE email = 'admin@aethra.local'" >/dev/null
  RESP=$(curl -s -c "$COOKIES" -X POST "$API/auth/login" -H 'content-type: application/json' \
    -d '{"email":"admin@aethra.local","password":"aethra-dev"}')
  echo "Retry: $RESP"
fi

step "2. Settings: environments"
curl -s -b "$COOKIES" -X POST "$API/api/settings/environments" -H 'content-type: application/json' -d '{"slug":"production","displayName":"Production"}' >/dev/null
curl -s -b "$COOKIES" -X POST "$API/api/settings/environments" -H 'content-type: application/json' -d '{"slug":"staging","displayName":"Staging"}' >/dev/null
curl -s -b "$COOKIES" -X POST "$API/api/settings/environments" -H 'content-type: application/json' -d '{"slug":"preview","displayName":"Preview"}' >/dev/null
echo "Envs created"

step "3. Create Project"
RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/projects/" -H 'content-type: application/json' -d '{"slug":"empresa-a","name":"Empresa A"}')
PRJ_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "PRJ_ID=$PRJ_ID"

step "4. Create Template + env mapping + auto-preview"
RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/projects/$PRJ_ID/templates" -H 'content-type: application/json' -d '{
  "slug":"webapp","name":"Web App","gitRepoUrl":"https://github.com/aethra/sample-app","branch":"main",
  "buildType":"Dockerfile","dockerfilePath":"Dockerfile"
}')
echo "$RESP"
TPL_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
SECRET=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['webhookSecret'])")
echo "TPL_ID=$TPL_ID  SECRET=$SECRET"

curl -s -b "$COOKIES" -X PATCH "$API/api/templates/$TPL_ID/environment-mapping" -H 'content-type: application/json' -d '{
  "mappings":[{"environment":"production","branch":"main"},{"environment":"staging","branch":"develop"}]
}' -w "Mapping HTTP: %{http_code}\n"

curl -s -b "$COOKIES" -X PATCH "$API/api/templates/$TPL_ID/auto-preview" -H 'content-type: application/json' -d '{"enabled":true}' -w "AutoPreview HTTP: %{http_code}\n"

step "5. Create Client + VM"
RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/projects/$PRJ_ID/clients" -H 'content-type: application/json' -d '{"slug":"empresa-a-tenant","displayName":"Empresa A"}')
CLI_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "CLI_ID=$CLI_ID"

RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/vms/" -H 'content-type: application/json' -d '{"name":"vm-main"}')
VM_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['vmId'])")
echo "VM_ID=$VM_ID"

step "6. Create Instances prod + staging (no TrackedRef explicit)"
RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/templates/$TPL_ID/instances" -H 'content-type: application/json' -d "{
  \"clientId\":\"$CLI_ID\",\"environment\":\"production\",\"targetVmId\":\"$VM_ID\",\"autoDeployOnNewBuild\":true
}")
PROD_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "PROD_ID=$PROD_ID"

RESP=$(curl -s -b "$COOKIES" -X POST "$API/api/templates/$TPL_ID/instances" -H 'content-type: application/json' -d "{
  \"clientId\":\"$CLI_ID\",\"environment\":\"staging\",\"targetVmId\":\"$VM_ID\",\"autoDeployOnNewBuild\":true
}")
STAGE_ID=$(echo "$RESP" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "STAGE_ID=$STAGE_ID"

step "7. Resolve effective trackedRef"
PROD_REF=$(curl -s -b "$COOKIES" "$API/api/instances/$PROD_ID" | python -c "import sys,json; print(json.load(sys.stdin)['effectiveTrackedRef'])")
STAGE_REF=$(curl -s -b "$COOKIES" "$API/api/instances/$STAGE_ID" | python -c "import sys,json; print(json.load(sys.stdin)['effectiveTrackedRef'])")
echo "prod.effectiveTrackedRef = $PROD_REF (expected refs/heads/main)"
echo "stage.effectiveTrackedRef = $STAGE_REF (expected refs/heads/develop)"
[ "$PROD_REF" = "refs/heads/main" ] || fail "prod ref unexpected"
[ "$STAGE_REF" = "refs/heads/develop" ] || fail "stage ref unexpected"

step "8. Push webhook to refs/heads/develop → only stage redeploys (via fan-out)"
BODY='{"ref":"refs/heads/develop","after":"abc123def456789012345678901234567890aabb","commits":[{"id":"abc123def456789012345678901234567890aabb","modified":["src/index.js"]}],"head_commit":{"id":"abc123def456789012345678901234567890aabb","modified":["src/index.js"]},"pusher":{"name":"smoke-test"},"repository":{"clone_url":"https://github.com/aethra/sample-app","html_url":"https://github.com/aethra/sample-app","full_name":"aethra/sample-app","ssh_url":"git@github.com:aethra/sample-app.git"}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')
RESP=$(curl -s -X POST "$API/webhooks/git" \
  -H "content-type: application/json" -H "X-GitHub-Event: push" -H "X-Hub-Signature-256: sha256=$SIG" \
  -d "$BODY")
echo "$RESP"
echo "$RESP" | grep -q '"matched_templates":1' || fail "push develop must match 1 template"

step "9. PR webhook for user without GitHubUsername mapping"
PR_BODY='{"action":"opened","number":42,"pull_request":{"number":42,"title":"feat: x","html_url":"https://github.com/aethra/sample-app/pull/42","user":{"login":"random-user"},"head":{"ref":"feature/x","sha":"deadbeef000000000000000000000000aabb1234"},"labels":[]},"repository":{"clone_url":"https://github.com/aethra/sample-app","html_url":"https://github.com/aethra/sample-app","full_name":"aethra/sample-app","ssh_url":"git@github.com:aethra/sample-app.git"}}'
SIG=$(printf '%s' "$PR_BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')
RESP=$(curl -s -X POST "$API/webhooks/git" \
  -H "content-type: application/json" -H "X-GitHub-Event: pull_request" -H "X-Hub-Signature-256: sha256=$SIG" \
  -d "$PR_BODY")
echo "$RESP"
echo "$RESP" | grep -q "github_user_not_mapped" || fail "PR for unmapped user must report github_user_not_mapped"

step "10. Map admin user to github username 'random-user' via PATCH /auth/me/profile"
curl -s -b "$COOKIES" -X PATCH "$API/auth/me/profile" -H 'content-type: application/json' \
  -d '{"GitHubUsername":"random-user"}' -w "HTTP: %{http_code}\n"
ME=$(curl -s -b "$COOKIES" "$API/auth/me" | python -c "import sys,json; d=json.load(sys.stdin); print(d.get('gitHubUsername'))")
echo "me.gitHubUsername = $ME (expected random-user)"
[ "$ME" = "random-user" ] || fail "gitHubUsername not persisted"

step "11. Retry PR webhook → should create Instance preview-pr-42"
SIG=$(printf '%s' "$PR_BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')
RESP=$(curl -s -X POST "$API/webhooks/git" \
  -H "content-type: application/json" -H "X-GitHub-Event: pull_request" -H "X-Hub-Signature-256: sha256=$SIG" \
  -d "$PR_BODY")
echo "$RESP"
echo "$RESP" | grep -q '"action":"created"' || fail "PR opened must create instance"

step "12. List previews via /api/instances?ephemeral=true"
RESP=$(curl -s -b "$COOKIES" "$API/api/instances?ephemeral=true")
echo "$RESP" | python -m json.tool | head -30
COUNT=$(echo "$RESP" | python -c "import sys,json; print(len(json.load(sys.stdin)))")
echo "ephemeral instances count = $COUNT"
[ "$COUNT" = "1" ] || fail "should have 1 ephemeral instance"

step "13. PR closed → preview is removed"
CLOSE_BODY='{"action":"closed","number":42,"pull_request":{"number":42,"title":"feat: x","html_url":"https://github.com/aethra/sample-app/pull/42","user":{"login":"random-user"},"head":{"ref":"feature/x","sha":"deadbeef000000000000000000000000aabb1234"},"labels":[]},"repository":{"clone_url":"https://github.com/aethra/sample-app","html_url":"https://github.com/aethra/sample-app","full_name":"aethra/sample-app","ssh_url":"git@github.com:aethra/sample-app.git"}}'
SIG=$(printf '%s' "$CLOSE_BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')
RESP=$(curl -s -X POST "$API/webhooks/git" \
  -H "content-type: application/json" -H "X-GitHub-Event: pull_request" -H "X-Hub-Signature-256: sha256=$SIG" \
  -d "$CLOSE_BODY")
echo "$RESP"
echo "$RESP" | grep -q '"status":"Removed"' || fail "PR closed must remove preview"

step "14. Confirm preview is gone"
RESP=$(curl -s -b "$COOKIES" "$API/api/instances?ephemeral=true")
COUNT=$(echo "$RESP" | python -c "import sys,json; print(len(json.load(sys.stdin)))")
echo "ephemeral count after closed = $COUNT (expected 0)"
[ "$COUNT" = "0" ] || fail "preview not cleaned up"

echo
echo "==== ALL 14 STEPS PASS ===="
