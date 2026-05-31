# Migración Coolify → Aethra

Guía operativa para migrar manualmente cada uno de tus proyectos desde Coolify (más Traefik, Uptime Kuma y Beszel) hacia Aethra. **No hay importer automático en MVP** — se migra proyecto a proyecto con esta secuencia, validando que cada uno queda en verde antes de pasar al siguiente.

Aplica a topología típica: 3 VMs Oracle Cloud (1 controladora con Coolify + 2 satélites con Beszel agent), con dominios en Cloudflare proxied.

---

## 0. Preparación (una sola vez)

1. **Levanta Aethra** en VM-1 (controladora):
   ```bash
   docker compose -f deploy/docker-compose.yml up -d
   ```
   Espera que `aethra-api` aplique migraciones EF (10 contexts) y `aethra-web` esté listo.

2. **Crea cuenta single-user**: visita `http://<vm1-ip>/auth/login` y entra con las credenciales de `Identity:DefaultUser` en `appsettings.json`.

3. **Registra los satélites** desde la UI `/vms/new`:
   - Pulsa "Agregar VM", copia el one-liner generado.
   - SSH a VM-2 y VM-3, ejecuta el one-liner. El binario del satélite se instala como systemd unit y abre el `HubConnection` contra el central.
   - Verifica en `/vms` que aparecen "Online" con métricas (CPU/RAM/Disk) en vivo.

4. **Conecta tu zona Cloudflare**:
   - En CF dashboard → API tokens → crea token con `Zone:Read + DNS:Edit` solo para tu zona.
   - En Aethra `/cloudflare/new`: pega `zone_id` (lo ves en el sidebar derecho de la zona en CF dashboard) y el `api_token`. Aethra hará un fetch inicial de la zona.

5. **Crea una API key para tu CLI/MCP** (opcional pero útil):
   - `/settings/api-keys/new` → name `migracion-manual`, scopes `*` (admin durante migración; revoca después).
   - Guarda el secret `aethra_...` UNA SOLA VEZ. Después no se puede recuperar.

---

## 1. Por cada proyecto en Coolify

### 1.1 Inventario rápido (extraer datos del Coolify actual)

En el dashboard de Coolify, abre el proyecto y anota:

| Dato | Dónde está en Coolify | Dónde va en Aethra |
|---|---|---|
| Nombre del proyecto | "Project name" | `POST /api/projects` body.name |
| Git repo URL | "Source → Git repository" | `Application.source.git_repo_url` |
| Branch | "Source → Branch" | `Application.source.branch` |
| Webhook secret | "Webhooks → Secret" | `Application.source.webhook_secret` |
| Dockerfile path | "Build → Dockerfile location" | `Application.build.dockerfile_path` |
| Build args | "Environment → Build-time" | env vars con `is_build_time=true` |
| Runtime env vars | "Environment → Runtime" | env vars con `is_runtime=true` |
| Secrets | "Environment → Secret" | env vars con `is_secret=true` |
| Dominios | "Domains" (FQDN list) | `Application.domains[]` |
| Cloudflare records | CF Dashboard | DNS records en `/cloudflare/{zone}/records` |
| Healthcheck | "Configuration → Healthcheck" | `Application.runtime.healthcheck` |
| Postgres/Redis bindings | "Resources" tipo Postgres/Redis | `ServiceBinding` en F5 (ver §1.6) |
| Notas / contexto | "Notes" (si existen) | `Note` (F6) en `/projects/{id}/notes` |
| Uptime monitor URL | Uptime Kuma | `Monitor` (F6) en `/monitors/new` |

### 1.2 Crea el Project en Aethra

UI: `/projects/new`. O via curl con tu API key:

```bash
curl -X POST http://<vm1-ip>/api/projects \
  -H "Authorization: Bearer aethra_..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Mi Proyecto",
    "slug": "mi-proyecto",
    "color": "#7c3aed"
  }'
```

Anota el `project_id` devuelto (formato `prj_01H...`).

Aethra crea automáticamente un Environment `production`. Si en Coolify tenías staging, crea otro:

```bash
curl -X POST http://<vm1-ip>/api/projects/{project_id}/environments \
  -H "Authorization: Bearer aethra_..." \
  -d '{"name":"staging"}'
```

### 1.3 Crea cada Application

Si en Coolify el proyecto era **un solo repo con un Dockerfile**: una sola Application.
Si era **monorepo con N subdirectorios deployables**: una Application por subdirectorio (cada uno con su `base_directory` y `watch_paths`).

```bash
curl -X POST http://<vm1-ip>/api/projects/{project_id}/environments/{env_id}/applications \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "name": "backend",
    "slug": "backend",
    "source": {
      "git_repo_url": "https://github.com/usuario/mi-saas",
      "branch": "main",
      "webhook_secret": "<copia del coolify>",
      "base_directory": "/backend",
      "watch_paths": ["backend/**"]
    },
    "build": {
      "type": "Dockerfile",
      "dockerfile_path": "Dockerfile"
    },
    "runtime": {
      "target_vm_id": "vm_01H...",
      "container_name": "backend",
      "ports": [{ "container_port": 8080 }]
    }
  }'
```

Si el repo tiene 2 Dockerfiles, repite el comando con `backend` y `frontend`, cada uno con su `base_directory` y `watch_paths`.

### 1.4 Carga env vars

Usa el endpoint batch (idempotente):

```bash
curl -X POST http://<vm1-ip>/api/applications/{app_id}/env-vars \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "vars": [
      { "key": "DATABASE_URL", "value": "...", "is_runtime": true, "is_secret": true },
      { "key": "FEATURE_X_ENABLED", "value": "true", "is_runtime": true }
    ]
  }'
```

O importa un `.env` desde la UI: `/applications/{app_id}` → "Editar env vars" → pegar el contenido `.env` → click "Importar".

### 1.5 Adjunta dominio + ruta + monitor en un solo paso (MCP)

Si tienes tu API key con scopes, esto es **un solo tool call** vía MCP:

```bash
curl -X POST http://<vm1-ip>/mcp \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "method": "tools/call",
    "params": {
      "name": "aethra_attach_domain",
      "arguments": {
        "application_id": "app_01H...",
        "hostname": "api.miproyecto.com",
        "cloudflare_zone_id": "cfz_01H..."
      }
    }
  }'
```

Esto orquesta:
- `Cloudflare.CreateDnsRecord` (CNAME apuntando a la IP de la VM target, proxied).
- `Proxy.Route` (hostname → containerName:port en YARP).
- TLS automático (Certes solicita cert ACME en background).
- Monitor opcional si pasas `create_monitor: true`.

### 1.6 Servicios compartidos (Postgres/Redis/RabbitMQ)

Si tu app en Coolify usaba un Postgres compartido:

1. Crea el `ManagedService` desde plantilla (UI `/services/new`):
   - Tipo: Postgres 16.
   - Slug: `postgres-main`.
   - Target VM: la misma donde lo tenías en Coolify (típicamente VM-1).

2. Aethra arrancará el contenedor con admin password autogenerado y cifrado en DataProtection. Status pasa a `Ready`.

3. **Migra los datos** del Postgres viejo al nuevo. Dos opciones:

   **a) Si Postgres viejo está corriendo bajo Coolify:**
   ```bash
   # Desde la VM con Coolify, dump con admin de Coolify
   docker exec coolify-postgres pg_dump -U postgres miapp_db > /tmp/miapp.sql

   # Copia el sql a la VM con Aethra (si es la misma, omite el scp)
   scp /tmp/miapp.sql user@vm1:/tmp/

   # Restaura. Necesitas el admin password de Aethra postgres-main:
   # UI: /services/postgres-main → "Mostrar credenciales admin" (requiere cookie auth).
   docker exec -i aethra-postgres-main psql -U postgres < /tmp/miapp.sql
   ```

   **b) Si la BD en Coolify está vacía o nueva**: skip; Aethra creará la BD limpia al hacer el binding.

4. Crea el `ServiceBinding` desde la app:
   ```bash
   curl -X POST http://<vm1-ip>/api/services/{service_id}/bindings \
     -H "Authorization: Bearer aethra_..." \
     -d '{
       "application_id": "app_01H...",
       "resource_name": "miapp_db",
       "permissions": "owner",
       "env_var_prefix": "",
       "migrations_hook": {
         "command": "dotnet App.dll --migrate",
         "timeout_seconds": 120,
         "fail_deploy_on_error": true,
         "run_on": "each_deploy"
       }
     }'
   ```

   Aethra:
   - `CREATE DATABASE miapp_db` (si no existe).
   - `CREATE USER miapp_db_user WITH PASSWORD '<random>'`.
   - `GRANT ALL ON DATABASE miapp_db TO miapp_db_user`.
   - Inyecta automáticamente `DATABASE_URL`, `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` en la app con `source=binding:bnd_...`.

5. **Borra a mano las env vars manuales** que tenías en Coolify para esa misma conexión (`DATABASE_URL`, etc.). La de Aethra las sobreescribe limpiamente porque tu manual tenía `source=null` — pero deja redundancia confusa.

Repite §1.6 para Redis (`redis-7` template) y RabbitMQ si aplican.

### 1.7 Migra notas y "facts" pegajosos

Si tenías notas/comentarios fuera de Coolify (Notion, Google Doc, post-its), pásalos a `/projects/{id}/notes`:

```bash
curl -X POST http://<vm1-ip>/api/notes \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "scope_type": "Project",
    "scope_id": "prj_01H...",
    "title": "Migración desde Coolify",
    "markdown_body": "Migrado el 2026-XX-XX. **Coolify Project ID**: 42. **Notas**: ..."
  }'
```

Las contraseñas/tokens pegajosos van como `PinnedFact` (cifrado):

```bash
curl -X PUT http://<vm1-ip>/api/pinned-facts \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "scope_type": "Project",
    "scope_id": "prj_01H...",
    "key": "admin_backoffice_password",
    "value": "<plain>",
    "is_secret": true,
    "description": "Para el panel /admin del backend"
  }'
```

### 1.8 Crea el monitor uptime (reemplaza Uptime Kuma)

```bash
curl -X POST http://<vm1-ip>/api/monitors \
  -H "Authorization: Bearer aethra_..." \
  -d '{
    "slug": "miapp-api",
    "name": "MiApp API",
    "url": "https://api.miproyecto.com/health",
    "http_method": "GET",
    "expected_status_codes": [200],
    "interval_sec": 60,
    "timeout_ms": 5000,
    "application_id": "app_01H..."
  }'
```

Verifica en `/monitors/{id}` que el primer check pasa en verde.

### 1.9 Primer deploy con Aethra

Desde la UI `/applications/{app_id}/deploys` → "Trigger deploy" (manual) o **haz un `git push` que toque tus `watch_paths`** y deja que el webhook lo dispare.

- Coolify webhook viejo: bórralo del repo GitHub (o cambia su URL al endpoint Aethra `/webhooks/git`).
- Aethra acepta `Authorization` con el `webhook_secret` que copiaste en §1.3.

Sigue los logs en vivo en `/deploys/{job_id}`. State machine: `Queued → Cloning → Building → Healthcheck → Swapping → Completed`.

### 1.10 Switch DNS final + apaga el viejo

Cuando el monitor esté en verde y el contenedor en `/applications/{id}` muestre el `git_sha` correcto:

1. **Si Cloudflare DNS ya apunta a la VM de Aethra**: nada que hacer, ya está sirviendo.
2. **Si todavía apunta al Coolify**: edita el record en `/cloudflare/{zone}/records/{record}` cambiando el `content` a la nueva IP. Aethra hace el PATCH al API de Cloudflare, propagación normalmente en <1min con proxied=true.
3. **Para containers**: detén el viejo en Coolify (botón Stop) — Aethra ya está sirviendo el tráfico vía YARP.

Confirma 24h con `/monitors/{id}` en verde + métricas `/vms/{id}` sin spikes raros.

### 1.11 Apaga las herramientas viejas (cuando todos los proyectos estén migrados)

En orden, **proyecto por proyecto migrado, no todo de golpe**:

1. **Coolify**: detén el contenedor del proyecto en Coolify dashboard. No lo borres aún (rollback).
2. **Traefik**: ya no recibe tráfico de ese hostname. Si era la única fuente, apágalo después de migrar todos.
3. **Uptime Kuma**: borra el monitor de ese proyecto.
4. **Beszel agents**: cuando todos los satélites Aethra estén operativos y verificados, apaga el agente Beszel en cada VM.

Cuando los 4 servicios viejos no tengan inquilinos:

```bash
# En la VM-1 con todas las herramientas viejas
docker stop coolify traefik uptime-kuma beszel
docker rm coolify traefik uptime-kuma beszel
# Backup de volúmenes ANTES de borrarlos:
tar czf /backups/old-stack-$(date +%Y%m%d).tar.gz /var/lib/coolify /var/lib/traefik /var/lib/uptime-kuma
docker volume rm coolify_data traefik_acme ... # solo después del backup
```

---

## 2. Rollback de un proyecto

Si algo falla DESPUÉS de cambiar DNS:

1. En Cloudflare → cambia el record de vuelta a la IP del Coolify viejo. Propaga en <1min.
2. En Coolify → arranca de nuevo el contenedor del proyecto (sigue ahí, no borraste nada).
3. En Aethra → no necesitas tocar nada; los containers quedan apagados.
4. Diagnostica con calma. Re-intenta cuando esté arreglado.

Por eso §1.11 dice "no borres Coolify hasta migrar TODO".

---

## 3. Migración del usuario (no del código)

Cosas que solo tú haces, una vez:

- **Mover claves SSH** que tenías guardadas en Coolify a un secrets manager o pegarlas como `PinnedFact` cifrado en el proyecto correspondiente.
- **Importar la lista de IPs de oncall/Discord/email** a la futura tabla `Notifications` (F6.5 — aún no existe, anota en la nota del proyecto).
- **Documentar cualquier cron/job manual** que tenías en Coolify "Scheduled tasks" — F8.5 podría modelar `CronJob` como otro tipo de Application.

---

## 4. Checklist por proyecto (copia-pega)

```
[ ] §1.1 Inventario completo de datos en Coolify
[ ] §1.2 Project creado en Aethra
[ ] §1.3 Application(s) creada(s) — una por Dockerfile/monorepo subdir
[ ] §1.4 Env vars cargadas (build-time + runtime + secrets)
[ ] §1.5 Dominio adjuntado (DNS + Route + TLS)
[ ] §1.6 Service bindings (Postgres/Redis/Rabbit) + data migrada
[ ] §1.7 Notas y pinned facts pasados
[ ] §1.8 Monitor uptime creado y verde
[ ] §1.9 Primer deploy con Aethra exitoso
[ ] §1.10 DNS switcheado + Coolify detenido
[ ] §1.11 (cuando aplique) Stack viejo apagado del todo
```

---

## 5. Referencias

- Endpoints REST: `/openapi/v1.json` (Swagger nativo .NET 10).
- MCP tools: `POST /mcp` con `Authorization: Bearer aethra_...`. Las tools `aethra_*` están documentadas vía MCP Inspector o llamando `tools/list`.
- Logs operativos: `journalctl -u aethra-api -f` en VM-1.
- Métricas de la propia instancia Aethra: `/vms/{vm1-id}` (los satélites Aethra reportan al hub central que también puede ver su propia VM si registras VM-1 como satélite local).
