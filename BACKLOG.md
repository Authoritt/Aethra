# Aethra — Backlog

Estado al cierre de F12 (2026-06-03). 59 commits locales sin push esperando autorización.

> **Convención**: P0 = bloqueante para producción · P1 = ventaja competitiva alta · P2 = compromisos técnicos documentados · P3 = cosmético

---

## P0 — Validaciones críticas pendientes

Cosas que están **codificadas** pero **no validadas E2E contra runtime real**.

### P0.1 — Validar SSH auto-provision con VM Linux real
- **Origen**: F11.4. Smoke se hizo con host SSH dummy (`1.2.3.4`) — solo se verificó el error path "ssh_connect_failed".
- **Qué falta**: probar contra una VM Linux real con SSH habilitado. Conectar, descargar binario `linux-x64`/`linux-arm64`, escribir systemd unit, verificar que el satélite se conecta al hub.
- **Archivos relevantes**: `src/Aethra.Modules.Vms/Infrastructure/Provisioning/RenciSshProvisioner.cs`, `scripts/install-satellite.sh`, `scripts/publish-satellite.sh`.
- **Esfuerzo**: medio día si hay VM disponible.

### P0.2 — Validar Build/Deploy E2E con Docker daemon real
- **Origen**: merge backend (`670f1ba` clone real + `1b9bd79` deploy real + `a7f1dc5` service provisioning real).
- **Qué falta**: ejecutar el flow `git push → Build → Deployment → container corriendo` contra una VM con Docker daemon vivo.
- **Bloqueo actual**: máquina de desarrollo local sin Docker/Podman ni virtualización en BIOS.
- **Esfuerzo**: 1 día (incluye preparar VM + smoke).

### P0.3 — Tests automatizados (gap del audit F9.9)
- **Origen**: F9.9 code review reveló cero unit tests + cero integration tests.
- **Hoy**: solo 2 NetArchTest (`ModuleIsolationTests`, `DomainPurityTests`).
- **Qué hace falta**:
  - Unit tests de aggregates puros (sin EF): `DeployJob`, `Build`, `Deployment`, `Monitor`, `EnvVarResolver`, `Instance.ResolveTrackedRef`.
  - Integration tests por módulo con EF + Postgres real via `Testcontainers.PostgreSql` (ya en `Directory.Packages.props`).
  - E2E con API + Satellite real (compose de test).
- **Esfuerzo**: 3-4 días (suite base + cobertura de los flows críticos).

---

## P1 — Features competitivas vs Dokploy

Identificadas en el audit comparativo post-F11. Items que mueven la aguja para presentar a Aethra como alternativa superior.

### F12.4 — Terminal access UI
- **Por qué**: Dokploy tiene botón "Terminal" en cada service que abre xterm.js sobre WebSocket → `docker exec`. Aethra no tiene exec interactivo (solo log streaming read-only).
- **Tech**: xterm.js client + nuevo método `ISatelliteRpcClient.OpenExecStreamAsync(vmId, containerId, command, IObserver<ExecChunk>)` que abre un stream bidireccional sobre el mismo SignalR hub.
- **Esfuerzo**: 2 días.

### F12.5 — Heroku Buildpacks + Railpack
- **Por qué**: Dokploy soporta 3 build systems auto-detect (Nixpacks ✓ + Buildpacks + Railpack). Aethra solo Nixpacks (F11.2).
- **Tech**: extender `BuildMode` enum + branch en `BuildOrchestrator.MapBuildMode` + satellite invoca `pack build` (buildpacks) o `railpack build` (Ruby).
- **Esfuerzo**: 1 día.

### F12.6 — Service Clone + Command Palette
- **Service Clone**: botón "Duplicar" en un service que crea una copia con env vars (útil para preview manual o split testing). Modelar como `CloneInstanceCommand(sourceId, newName, ...)`.
- **Command Palette `Ctrl+K`**: lib `cmdk` (ya planeada en F10 opcional, no entregada). Quick navigation a project/template/instance/vm via fuzzy search.
- **Esfuerzo**: 2 días combinados.

### F12.7 — Enterprise (Audit + SSO + Whitelabel)
- **Audit logs completos**: hoy solo `idempotency_keys` registran traza. Tabla `audit_events` cross-module con quién, qué, cuándo, IP, user agent. Endpoint `/api/audit/events` con filtros.
- **SSO OIDC/SAML**: integración con providers externos (Auth0, Okta, Azure AD, Keycloak). Library: `Microsoft.AspNetCore.Authentication.OpenIdConnect`.
- **Whitelabel**: logo + colores corporativos custom. Endpoint `/api/settings/branding` + override del Logo en runtime.
- **Esfuerzo**: 5 días combinados.

---

## P2 — Compromisos técnicos documentados

Cosas conscientemente dejadas como "good enough by now" durante F11/F12. Ya funcionan pero tienen una forma "más correcta" pendiente.

### Backups (F11.3B)

| Item | Estado actual | Forma correcta |
|---|---|---|
| Postgres backup | Formato custom `aethra-pg-dump v1` via Npgsql `COPY TO STDOUT`. Restore solo restaura data, no schemas. | `docker exec <container> pg_dumpall` via satellite RPC. Restore con `psql < dump.sql`. |
| S3 destination | Stub HTTP sin SigV4 | Integrar `Minio` C# library o `AWSSDK.S3`. |
| Cron parser (BackupWorker) | Solo soporta `*/N` (interval minutes) | NCrontab para cron expressions completas (`0 2 * * 1`). |
| Redis backup | SCAN lógico + JSON gzip | `BGSAVE` + copia binaria del `dump.rdb`. |

**Files**: `src/Aethra.Modules.Services/Infrastructure/Backups/*Engine.cs`.

### Cron parser (F12.1A)

`src/Aethra.Modules.Services/Infrastructure/Scheduling/CronExpression.cs` — parser propio que soporta:
- `*` cualquier valor.
- `N` literal.
- `*/N` cada N.
- `A-B` rango.
- `A,B,C` lista.

**NO soporta**: nombres (`MON-FRI`), `@daily`, `@hourly`, last day of month, etc.

**Migración**: NCrontab — mismo paquete que se planeó originalmente y se descartó por complejidad. Si necesitamos expresiones complejas, switchear.

### Marketplace (F12.2)

| Item | Estado |
|---|---|
| Supabase template | Stub single-container con `multi_container: true` flag. El engine no soporta docker-compose multi-service. |
| Provisioners faltantes | ClickHouse, MeiliSearch, MinIO, PocketBase tienen YAML pero `binding_supported: false`. Cuando un caso de uso requiera sub-resources por app, escribir `IServiceProvisioner` impl. |
| Backups por template | Hoy hardcoded en `BackupOrchestrator` por engine. Mover el comando de backup al YAML (`backup_command: pg_dumpall`) para que cada template declare cómo se backupea. |

### Branch + Preview (F12.3)

- **Backfill `tracked_ref` migration** usa `template.default_branch` directo. NO consulta `EnvironmentMapping`. Instances pre-existentes con `Environment="produccion"` y mapping `produccion→main` pueden quedar mal seteadas si `default_branch` difería. Operadores deben revisar manualmente.
- **401 transitorio Kestrel/auth warm-up**: observado en smoke E2E F12.3 durante el primer PATCH tras login. Mitigado en el script pero **no investigada la causa**. Hipótesis: race condition entre cookie issuance y request siguiente.
- **Client interno reservado**: usa slug `"preview"` (no `"__preview__"` literal). El regex del aggregate Client (`^[a-z][a-z0-9-]{0,30}$`) no permite underscores. Cosmético, no afecta funcionalidad.

### Identity / Auth (F11.1 + F12.3)

- `User.GitHubUsername` se setea manualmente (campo en perfil). Mejora opcional: GitHub OAuth login que auto-pobla el field al primer login (require ClientID/Secret de GitHub App).
- 2FA TOTP implementado en F12.1B pero recovery codes guardan en bitmask (`TotpRecoveryCodesUsedMask`). 10 codes max — si se usan todos, regen obligado.

---

## P3 — Cosmético / pulido

- **Lighthouse a11y >= 90** + responsive smoke en 375×812 (F10.5 — marcado completed con asunción, nunca ejecutado).
- **Env label del sidebar** (`apps/web/components/layout/app-sidebar.tsx:82-92`): hoy hardcoded leyendo `NEXT_PUBLIC_ENV`. AGENTS.md menciona que debe conectarse al endpoint `/context` real para mostrar el environment actual del servidor.

---

## Operación

### Commits sin push (59)

Esperando autorización del usuario. Branches sincronizadas, build verde, working tree limpio.

Resumen por fase:
- F11.4 (SSH provision): 5 commits
- F11.5 (MCP tools): 5 commits
- F11.6 (i18n primera pasada): 4 commits
- F11.7 (MCP scope auth fix): 1 commit
- F11.8 (ContextTools race fix): 1 commit
- F11.6.5 (i18n segunda pasada): 10 commits
- F11.6.6 (i18n forms internos): 12 commits
- F11.6.7 (i18n cleanup final): 4 commits
- F12.1 (Scheduled Jobs + 2FA): 9 commits
- F12.2 (Templates marketplace): 3 commits (+ 1 mezclado con F12.3 por race de `git add -A`)
- F12.3 (Branch + Preview por PR): 5 commits

---

## Prioridades sugeridas

Si querés ir cerrando esto en orden de impacto:

1. **Antes de productivo real** → P0.1 (SSH validation) + P0.2 (Build/Deploy real). Hay que tener UNA VM Linux donde Aethra haga el flow completo end-to-end.
2. **Antes de venderlo como producto** → P0.3 (test suite) + F12.7 (audit logs + SSO).
3. **Para diferenciarse de Dokploy** → F12.4 (Terminal UI) + F12.6 (Command Palette).
4. **Para escalar** → F12.5 (Buildpacks) + P2 (backups productivos reales).

---

## Histórico (cerrado, referencia)

- **F0-F8** MVP: Coolify+Traefik+Uptime Kuma+Beszel replacement
- **F9.0-F9.10** Multi-tenant refactor + ContainerRuntime + Settings + secrets + push GitHub
- **F10.0-F10.6** UI rework: shadcn + 3 temas + sidebar + 50+ pages al DS
- **F11.1-F11.8** Multi-user + Nixpacks + Notifications + Backups + SSH auto-provision + MCP tools (40) + i18n es/en
- **F12.1-F12.3** Scheduled Jobs + 2FA + Templates marketplace (15) + Branch-per-Instance + Preview por PR
