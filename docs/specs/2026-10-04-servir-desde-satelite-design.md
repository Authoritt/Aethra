# Aethra — servir apps desde una VM satélite (OT-007, fase D)

- **Fecha:** 2026-10-04 · **Base:** `main @ 3755923` · **Autor:** arquitecto (read-only) · **Estado:** propuesta; el dueño delegó la ejecución (Codex implementa, Claude revisa).
- **Orden:** `AgentHub/ordenes/Aethra/OT-007-servir-apps-desde-un-satelite.md`. Implementación: OT-008.
- **Normativa:** `AgentHub/docs-arquitectura/` (cap. 11 arquitectura mínima suficiente/YAGNI; cap. 06 "Estrategia: cuándo NO" con una sola variante), `docs/F13-compose-native-deploy.md`, OT-001, OT-006.
- **Clasificación:** extensión dentro del monolito modular existente. Cero servicios/componentes de runtime nuevos (el conector `cloudflared` ya es contenedor gestionado desde F13.11).

**Recomendación en una línea:** cada VM satélite tiene su propio túnel Cloudflare, con su conector como contenedor DENTRO de `aethra-net` del satélite, e ingress por hostname directo a `http://{slug}-{svc}:{port}` (DNS embebido de Docker). Sin proxy local, sin puertos publicados, sin Tailscale en el camino de la petición; el tráfico nunca toca `principal`. Todo lo existente sigue por el camino actual, que solo cambia para una VM con un túnel registrado en modo `Direct`.

## 0. Hechos medidos que condicionan el diseño

| # | Hecho | Evidencia |
|---|---|---|
| H1 | Backend de ruta = `http://{slug}-{svc}:{port}`, resuelto por el YARP del central vía DNS de `aethra-net` de `principal` | `apps/api/Bootstrap/NativeDeployRunner.cs:589-592` |
| H2 | Contenedores/build/cleanup/healthcheck ya van a `instance.TargetVmId` | `NativeDeployRunner.cs:174,198,282,293,319,373-374` |
| H3 | Todo el módulo de túnel asume UN túnel: "el primero por `CreatedAt`" | `UseCases/Tunnels/Commands/TunnelIngressCommands.cs:35`; `Queries/GetTunnelQuery.cs:22`; `Queries/GetConnectorTokenQuery.cs:64`; `Commands/DeleteTunnelCommand.cs:102-104` |
| H4 | La regla de ingress siempre apunta a `tunnel.AethraService` (`http://localhost:5080`); si el host ya existe es no-op aunque apunte a otro servicio | `TunnelIngressCommands.cs:71-77`; default `Domain/CloudflareTunnel.cs:75-76` |
| H5 | Escribir ingress = PUT que REEMPLAZA la config entera (read-modify-write) | `Infrastructure/Cloudflare/HttpCloudflareApiClient.cs:185-206` |
| H6 | El conector se despliega con `NetworkName: "host"` y toma el `TargetVmId` del primer túnel | `apps/api/Bootstrap/CloudflareConnectorDeployer.cs:31-36,54-65` |
| H7 | Auto-DNS usa un CNAME fijo de config (`NativeDeploy:TunnelCname`); si existe CUALQUIER record con ese nombre lo salta sin mirar a dónde apunta; best-effort (log-only) | `NativeDeployRunner.cs:68,727-730,752-757,766-774` |
| H8 | El paso DNS/ingress corre para TODOS los hosts tocados, aunque la ruta haya fallado | `NativeDeployRunner.cs:583-586,630-634` |
| H9 | `svc.Hostname` está a nivel de TEMPLATE: prod y staging del mismo template compartirían host | `src/Aethra.Shared.Contracts/Projects/ITemplateLookup.cs:61-72`; uso `NativeDeployRunner.cs:571` |
| H10 | `svc.Env` del template pisa la cascada (donde la Instance gana) | `NativeDeployRunner.cs:145-147,180-184`; F13 "Blocker" `docs/F13-compose-native-deploy.md:114-121`; mitigado por convención F13.6 (`:123-136`) |
| H11 | Slug de monitor en multi-host = primera etiqueta del host (`org`, `api`…): prod y staging chocan | `NativeDeployRunner.cs:668` |
| H12 | La limpieza de zombis borra con `force` todo `{slug}-*` que no esté en el template; `app` vs `app-staging` SÍ casan | `NativeDeployRunner.cs:463-480` |
| H13 | Docker liga puertos a `127.0.0.1` por defecto y usa `NetworkMode = spec.NetworkName`; no crea la red si falta | `apps/satellite/Containers/Docker/DockerContainerRuntime.cs:335-351,369`; ningún `CreateNetwork` en `apps/`/`src/` |
| H14 | El pull en `RunContainer` es ANÓNIMO y el deploy nativo pasa `pullFrom: null` | `DockerContainerRuntime.cs:907-925`; `NativeDeployRunner.cs:293` |
| H15 | Un servicio gestionado YA se coloca en una VM: corre vía RPC en `svc.TargetVmId` y en `aethra-net` | `src/Aethra.Modules.Services/UseCases/Commands/CreateServiceFromTemplateCommand.cs:49-50,85,103` |
| H16 | Aprovisionar bindings y backups de servicios gestionados abre Npgsql DIRECTO desde el central al nombre de contenedor: no alcanza otra VM | `Infrastructure/Provisioning/IManagedServiceHostResolver.cs:20-26`; `PostgresProvisioner.cs:53`; `Backup/PostgresBackupEngine.cs:33-38` |
| H17 | Los `ScheduledJob` ejecutan `exec` VÍA RPC en `svc.TargetVmId` → funcionan en un satélite | `Infrastructure/Scheduling/ScheduledJobWorker.cs:243,257`; `apps/api/Hubs/SignalRSatelliteRpcClient.cs:126-139` |
| H18 | El satélite conecta saliente a `{CentralUrl}/hubs/satellite` (bypass de Access); el instalador soporta `aarch64` | `apps/satellite/Workers/SatelliteConnectionWorker.cs:55`; `scripts/install-satellite.sh:68-74` |
| H19 | El instalador baja el binario de `/api/satellite/binary`: `AllowAnonymous` en la app pero NO en el bypass de Access | `scripts/install-satellite.sh:105-107`; `src/Aethra.Modules.Vms/Presentation/VmsEndpoints.cs:112,137` |
| H20 | En `principal` ya corre Tailscale; central con `NativeDeploy__TunnelCname=ca75b591-…` y `Deployments__AppNetwork=aethra-net` | SSH read-only 2026-10-04 |
| H21 | Las imágenes de InOneTickets se construyen `linux/arm64` en un workflow; CI apagado en todos los repos | `InOneTickets/.github/workflows/build-and-push.yml:53,82,111,140` |
| H22 | Staging de InOneTickets EXIGE otro dominio registrable (cookie) y secretos distintos | `InOneTickets/deploy/stack/docker-compose.staging.yml:40-49` |

## 1. Decisiones

### D1 — Cómo llega el tráfico a una app en un satélite

**Decisión: (b′) un túnel Cloudflare POR VM, con conector-contenedor en `aethra-net` del satélite e ingress por hostname a `http://{slug}-{svc}:{port}` resuelto por el DNS embebido de Docker.**

- Camino: DNS → borde Cloudflare → túnel de la VM → conector en la VM → contenedor. No toca `principal`: el satélite **sigue sirviendo si `principal` cae**.
- **Cero puertos publicados**: el conector sale hacia Cloudflare y entra a los contenedores por la red Docker. Invariante H13 intacta.
- Reutiliza F13 entero: `CloudflareTunnel.TargetVmId` (`Domain/CloudflareTunnel.cs:37-39`), ingress remoto zero-blip (F13.9), conector como contenedor gestionado (F13.11). Solo cambia la red del conector (`host` → `aethra-net`) y el `service` de cada regla.
- El backend de la `Route` es LITERALMENTE el `service` de la regla de ingress, así que `RouteOwnershipRules` sigue valiendo sin cambios (`src/Aethra.Modules.Proxy/UseCases/Routes/RouteOwnershipRules.cs:45-51`).
- **Límite declarado**: en un túnel `Direct` solo prefijo `"/"` por host (`TunnelIngressRule` no tiene `path`, `Application/Dtos/CloudflareTunnelDto.cs:19`). Un template con `PathPrefixes` ≠ `"/"` hacia un satélite **se rechaza antes de tocar contenedores**, con error explícito.

| Alternativa descartada | Por qué no |
|---|---|
| (a) túnel por VM + proxy local (Caddy/Traefik/YARP) | Un componente de runtime más y sincronización de config central→satélite solo para rutas por path que nadie pide. Cap. 06: con una sola variante no se abstrae. Se reabre con el primer inquilino de satélite que necesite split por path. |
| (b) ingress a `localhost:<puerto>` publicado en loopback | `HostPort: null` da puerto efímero (`NativeDeployRunner.cs:237-241`; `DockerContainerRuntime.cs:345-350`): cambia en cada deploy. (b′) consigue lo mismo sin puertos. |
| (c) central → satélite por Tailscale | Incumple el criterio (cae `principal` → cae el satélite), duplica la latencia Bogotá→São Paulo→Bogotá y exige publicar puertos en `tailscale0`. |

### D2 — Modelo en Aethra

**Decisión: un solo cambio de esquema — `CloudflareTunnel.Mode` (`CentralProxy` | `Direct`), default `CentralProxy` en la migración.** Lo demás se DERIVA:

- **En qué VM vive una ruta**: `Route.OperationalOwnerId` → Instance → `Instance.TargetVmId` (`Domain/Instances/Instance.cs:37`; ya se escribe en `NativeDeployRunner.cs:590-592`). Rutas sin dueño (manuales) = central.
- **Por qué túnel sale**: el túnel `Direct` cuyo `TargetVmId` = el de la Instance; si no hay → **camino actual, idéntico**. Lo decide una función pura (`EdgePlacementRules.Resolve`).
- Restricción: como mucho un túnel `Direct` por VM (índice único parcial `(target_vm_id) WHERE mode='direct'`).
- **Host por servicio y por instancia**: token `{domain}` en `svc.Hostname` = `CustomDomain ?? AutoHostname` de la instancia (misma idea que `{instance}`, ya interpolado en Env/volúmenes, `NativeDeployRunner.cs:183,226`). Prod (`org.{domain}` → `org.inonetickets.lat`) y staging salen del mismo template. Resuelve H9 sin tocar el esquema de template/instance.
- **Datos existentes**: la única migración es `ALTER TABLE cloudflare.tunnels ADD mode … DEFAULT 'central_proxy'` + el índice. Ni una fila de `proxy.routes`/instances/templates cambia.

| Alternativa descartada | Por qué no |
|---|---|
| `Route.EdgeTunnelId` | Duplica una verdad derivable; migra todas las rutas y toca `DatabaseProxyConfigProvider`, que sirve a TODO producción. |
| Ingress del túnel como única tabla de rutas | Las reglas no tienen `Origin`: se pierde la protección de propiedad de OT-001 (la clase de fallo que borró las rutas de Yunke). |
| `Instance.ServiceHostnames` (jsonb) | Más potente (alias `www`) pero migración + API + UI + MCP. `{domain}` cubre el caso real; `www` con regla de redirección de Cloudflare. |
| Dos templates (prod/staging) | Duplica ~38 claves: la desincronización silenciosa que `docker-compose.staging.yml:8-12` prohíbe. |

**Precedencia de Env del template (H10): se DECLARA, no se invierte** (invertirla cambia a Ekippo/Yunke: prohibido). `EnvPrecedenceRules.Merge(baseEnv, svcEnv, slug)` devuelve **el mismo diccionario que hoy** + la lista de claves en que `svc.Env` pisó un valor distinto de la cascada; cada una sale como warning del deploy. Regla de la fase M: en `svc.Env` solo claves comunes o con `{instance}`; hosts, cookie, secretos y cadena de conexión van en el Env de la Instance (F13.6).

### D3 — Base de datos en el satélite

**Decisión: un servicio gestionado `postgres` POR ENTORNO, colocado en Bogotá con `TargetVmId`, creado con lo que ya existe (`CreateServiceFromTemplateCommand.cs:74-85,103`).** Entra en `aethra-net` del satélite; la app lo alcanza por nombre.

- **Un contenedor por entorno**: aprovisionar bindings exige Npgsql desde el central (H16), que no alcanza Bogotá; con uno por entorno, base y rol nacen del init de la imagen. Da además el aislamiento que exige `docker-compose.staging.yml:40-49`. Coste ~100-200 MB RAM c/u sobre 24 GB.
- **Credenciales**: la app usa el admin de SU contenedor; se copian al Env de la Instance (supuesto S12: hay vía para leerlas descifradas).
- **Regla de nombres (H12)**: el slug de un servicio NO puede empezar por `{slugDeInstancia}-` de ninguna instancia de esa VM, o la limpieza de zombis lo borra con `force`. → `pg-iot-prod`, `pg-iot-stg`; instancias `iot-prod`, `iot-stg` (NUNCA `inonetickets` + `inonetickets-staging`).
- **Backups**: NO `BackupPolicy` en servicios remotos (su motor necesita conexión directa, H16). En su lugar un `ScheduledJob` con `exec` por RPC (H17): `pg_dump -Fc` a un volumen `/backups` del contenedor con rotación; cada corrida en `ScheduledJobRun` con exit code. Copia fuera de la VM: pregunta P2.

| Alternativa descartada | Por qué no |
|---|---|
| Postgres como servicio del template | `NativeDeployRunner` hace remove `force:true` + run en CADA deploy/webhook (`:177,282,293`): Postgres moriría a la fuerza en cada push. |
| `aethra-postgres` de `principal` desde Bogotá | Rompe "sirve si cae principal", latencia interregional, obliga a exponer Postgres (OT-002 lo dejó en loopback). |
| Provisión/backup desde el central por Tailscale | Trabajo real para lo que dos contenedores + un `ScheduledJob` resuelven ya. Se reabre con más de 3 bases en satélites. |

### D4 — Conectividad central ↔ satélite desde otra tenancy

**Decisión: basta el SignalR público; Tailscale NO es requisito de Aethra.**

- El satélite abre conexión SALIENTE a `wss://aethra.authoritforge.dev/hubs/satellite` (bypass de Access, H18): cero puertos entrantes en Bogotá (supuesto S5: salida 443 permitida).
- Por ese canal viajan ya todos los RPC (build, run, list, exec, logs, archivos); límite de mensaje 64 MiB (`apps/api/Program.cs:215`).
- Tailscale queda OPCIONAL para operación (SSH, copia de backups a micro-2 si P2 = a).

**Instalación ARM64**: (1) `apt install docker.io`; (2) `docker network create aethra-net` (obligatorio por H13); (3) registrar la VM (`POST /api/vms` → token); (4) binario `linux-arm64` de `scripts/publish-satellite.sh` copiado por **scp** (el one-liner con `curl` lo bloquea Access, H19/S4; mejor que abrir otro bypass); (5) `install-satellite.sh --central-url https://aethra.authoritforge.dev --token … --runtime docker` con el binario ya puesto — ajustar L105-107 para aceptar tarball local (carril propio).

**Atril (`atril.service`, `cloudflared-atril`) no se toca.** Docker añade cadenas iptables (S6: conviven con el `REJECT` de Oracle); se verifica en el gate de F4 con `systemctl is-active atril cloudflared-atril` + `curl` a `atril.authoritforge.dev` antes y después.

### D5 — Retrocompatibilidad (innegociable)

| # | Garantía | Mecanismo y prueba |
|---|---|---|
| R1 | Ninguna instancia existente cambia de camino | `Direct` solo si existe túnel `Mode=Direct` para `instance.TargetVmId`; hoy ninguno (la columna nace `central_proxy`). Test de `EdgePlacementRules` con ese estado → `Central`. |
| R2 | El camino `Central` es el código de hoy | En `NativeDeployRunner` el diff solo RAMIFICA; L589-663 y L706-775 se ejecutan igual para `Central`. G2 con regla explícita "rama Central byte-equivalente". |
| R3 | Handlers del túnel con argumentos nulos = hoy | `EnsureTunnelHostnameCommand(Hostname, TunnelId=null, Service=null)`: sin `TunnelId` → túnel `CentralProxy` más antiguo, `AethraService`, mismo no-op. Igual `RemoveTunnelHostname`, `GetTunnel`, `GetConnectorToken`, `DeleteTunnel(null)`. Filtrar por `CentralProxy` impide además que el túnel de Bogotá pase a ser "el primero". |
| R4 | El conector de `principal` no cambia | `CloudflareConnectorDeployer` sin argumentos → túnel `CentralProxy` + `NetworkName:"host"`; solo `Direct` usa `Deployments:AppNetwork`. |
| R5 | `{domain}` no altera hosts actuales | Solo se sustituye si aparece el literal (S2, verificado en F0). |
| R6 | La precedencia de Env no cambia | `EnvPrecedenceRules.Merge` devuelve el mismo diccionario; test de igualdad contra el algoritmo de `NativeDeployRunner.cs:180-184`; solo añade warnings. |
| R7 | Los monitores existentes no se duplican | `MonitorSlugRules`: sigue `host.Split('.')[0]` (L668); solo si `CreateMonitor` da `slug_taken` con OTRA URL (hoy ya `Failed`, `DeploySideEffectRules.cs:110-128`) se reintenta con `{slug}-{label}`. |
| R8 | Nada fuerza redeploy de apps | La migración no emite eventos ni toca instancias. Desplegar el central recrea `aethra-central` (segundos de corte del YARP) como cualquier release: en ventana del dueño (OT-001 "Prohibido"). |
| R9 | Prueba en producción tras F3 | Antes/después del deploy del central y tras un redeploy por webhook de una instancia de bajo riesgo (ParadoxBox): hash de `proxy.routes` igual; ingress de `authorit-apps` igual (`GET …/configurations`); `CreatedAt` de los contenedores de Yunke igual. |

### D6 — Plan por fases

Todas las decisiones van en **funciones puras dentro de módulos de librería** (patrón `RouteOwnershipRules` + tests). `apps/api` no tiene proyecto de tests (NU1109, OT-006 §3). Regla R2 de OT-006: cada función extraída cita **la línea donde `NativeDeployRunner` la llama**.

**F0 — verificación previa (orquestador, solo lectura).** S1-S5 en `aethra-postgres`, por `curl` y desde Bogotá. Gate: cada supuesto confirmado o el spec se corrige antes de la fase I.

**F1 — reglas puras + tests (cero llamadores en runtime).**

| Archivo nuevo | Decide |
|---|---|
| `src/Aethra.Modules.Deployments/Rollout/EdgePlacementRules.cs` | `Resolve(instanceVmId, tunnels)` → `Central`/`Direct(t)`/`Ambiguous`; `ValidateDirect(services, resolvedHosts)` (solo `"/"`; todo servicio con host público lo tiene resuelto); warning "instancia en VM sin túnel Direct: el proxy central no alcanza sus contenedores". |
| `src/Aethra.Modules.Deployments/Rollout/ServiceHostRules.cs` | token `{domain}` |
| `src/Aethra.Modules.Deployments/Rollout/EnvPrecedenceRules.cs` | merge idéntico + claves pisadas |
| `src/Aethra.Modules.Deployments/Rollout/MonitorSlugRules.cs` | slug de monitor con reintento |
| `src/Aethra.Modules.Cloudflare/UseCases/Tunnels/TunnelIngressPlanner.cs` | `Plan(current, host, service)` → `NoOp`/`Insert(idx)`/`Replace` (antes del catch-all); `PlanRemove` |
| `src/Aethra.Modules.Cloudflare/UseCases/DnsRecords/DnsRecordRules.cs` | `Classify(existing?, desiredTarget)` → `Create`/`Ok`/`Conflict` (CNAME a OTRO túnel = error, no se salta) |

Tests nuevos en `tests/Aethra.Modules.Deployments.Tests/` y `tests/Aethra.Modules.Cloudflare.Tests/`. Mínimos: estado actual → `Central`; `{domain}` ausente → host idéntico; merge = algoritmo L180-184; slug de monitor existente sin cambio; ingress con host a otro servicio → `Replace` (solo `Direct`); CNAME al túnel viejo → `Conflict`. Gate: `dotnet build Aethra.slnx` 0 errores y `dotnet test` ≥ línea base + nuevos, N/N.

**F2 — módulo Cloudflare.** `Domain/CloudflareTunnel.cs` (+`Mode`), `Domain/TunnelMode.cs` (nuevo), `Infrastructure/Configurations/CloudflareTunnelConfiguration.cs`, migración + `CloudflareDbContextModelSnapshot.cs`, `UseCases/Tunnels/Commands/{TunnelIngressCommands,RegisterTunnelCommand,DeleteTunnelCommand}.cs`, `UseCases/Tunnels/Queries/{GetTunnelQuery,GetConnectorTokenQuery}.cs` + `GetTunnelForVmQuery.cs` (nuevo), `Application/Dtos/CloudflareTunnelDto.cs`, `apps/api/Bootstrap/CloudflareConnectorDeployer.cs`, `src/Aethra.Modules.Mcp/Tools/CloudflareTools.cs` (parámetros opcionales), el fichero de endpoints de `/api/cloudflare/tunnel/*`, `apps/web/lib/types.ts` (+`mode`). Reglas: `Register` en `Direct` exige `TargetVmId` y `FallbackService="http_status:404"` (el default `https://localhost:443`, `CloudflareTunnel.cs:76`, daría 502 en Bogotá). Escritura de ingress serializada por túnel (`SemaphoreSlim` por `TunnelId`) y **relectura tras el PUT**: si la regla no está, error (A2). Gate: tests verdes; tests de handler con argumentos nulos sobre una API Cloudflare falsa demuestran R3.

**F3 — `NativeDeployRunner` (chokepoint).** Carril: SOLO `apps/api/Bootstrap/NativeDeployRunner.cs`. Resolver colocación ANTES de `Deployment.Queue` (L110); `Direct` + `ValidateDirect` fallido → `Fail` sin tocar nada. `EnvPrecedenceRules` en L180-184, `ServiceHostRules` en L571. En `ReconcileRoutingAsync`, rama `Direct`: rutas igual (L587-627); ingress+DNS SOLO para hosts con ruta `Created`/`AlreadyExists` (corrige H8 en `Direct`), con `TunnelId` y `Service=backend`; el fallo se PROPAGA como `routing.Error` (OT-006 #52); al borrar una ruta obsoleta en el paso 3, se quita su regla de ingress si el host ya no está deseado. Rama `Central`: sin cambios. Monitores con `MonitorSlugRules`. Gate: G2 adversarial con la rúbrica R1-R9; build + tests; deploy del central en ventana del dueño; pruebas R9 antes/después.

**F4 — alta de Bogotá (operación; código solo en `install-satellite.sh`).** Docker + `aethra-net`; satélite por scp; VM Connected; túnel `authorit-bogota` (uno por VM; el túnel caído `inonetickets` no se toca); registrado `Mode=Direct`, `TargetVmId=Bogotá`; conector en `aethra-net`; humo con un template desechable en `smoke-bogota.authoritforge.dev`. Gate: 200 desde fuera; conexiones del túnel SOLO desde la IP de Bogotá; ingress con `service: http://smoke…:port`; `docker ps` sin puertos publicados; Atril sano; build git verificado por el canal (S7); limpieza del humo.

**Fase M (OT aparte, InOneTickets).** Dos Postgres gestionados (`pg-iot-prod`, `pg-iot-stg`); template git de 4 servicios (tienda sin `svc.Hostname` → dominio de la instancia; `org.{domain}`, `admin.{domain}`, `api.{domain}`); instancias `iot-prod`/`iot-stg` con Env por instancia; `ScheduledJob` de `pg_dump` + copia fuera de la VM (P2); redirección `www` → apex; revisar los records DNS existentes de `inonetickets.lat` (`DnsRecordRules` dará `Conflict` si apuntan al túnel viejo).

## 2. Flujo de una petición

```
Navegador ─ https://org.inonetickets.lat ─▶ Cloudflare edge (TLS)
     DNS: org.inonetickets.lat CNAME <uuid-authorit-bogota>.cfargotunnel.com  (proxied)
                                   │
                                   ▼  túnel "authorit-bogota" (config remota, zero-blip)
VM Bogotá (158.247.121.125, otra tenancy) ─────────────────────────────────────────────
  aethra-cf-connector (cloudflared --token, red aethra-net, SIN puertos)
     ingress: org.inonetickets.lat → http://iot-prod-org:3001   (DNS embebido Docker)
                                   │
                                   ▼
  iot-prod-org ──▶ iot-prod-api:5091 ──▶ pg-iot-prod:5432        (todo en aethra-net)
──────────────────────────────────────────────────────────────────────────────────────
Plano de control (FUERA del camino de la petición):
  aethra-central (principal) ◀── wss saliente /hubs/satellite ── aethra-satellite (Bogotá)
  aethra-central ── API Cloudflare (PUT ingress, CNAME) ──▶ Cloudflare
  Si cae principal: Bogotá sigue sirviendo; se detienen deploys, monitores y ScheduledJobs.
```

## 3. Cambios por módulo

| Módulo | Cambio | ¿Esquema? |
|---|---|---|
| Cloudflare | `TunnelMode`; handlers con `TunnelId`/`Service` opcionales; filtro `CentralProxy` en las rutas nulas; `GetTunnelForVmQuery`; planner de ingress; reglas de DNS | Sí: 1 columna con default + 1 índice parcial |
| Deployments | 4 reglas puras en `Rollout/` | No |
| api/Bootstrap | Rama `Direct` en `NativeDeployRunner`; red del conector según el modo | No |
| Proxy | Ninguno (`Route`, `RouteOwnershipRules` intactos) | No |
| Projects | Ninguno (`{domain}` es interpretación del runner) | No |
| Services | Ninguno; sin `BackupPolicy` remota | No |
| Satellite | Solo `install-satellite.sh` acepta tarball local | No |
| Mcp / Web | Parámetros opcionales; `mode` en el DTO | No |

## 4. Riesgos

| Nivel | Riesgo | Mitigación |
|---|---|---|
| **ALTO A1** | Regresión en la rama `Central` de `NativeDeployRunner` → afecta a Yunke (DIAN) | Rama decidida por función pura; código `Central` sin tocar; G2; ventana del dueño; pruebas R9 |
| **ALTO A2** | Carrera read-modify-write en el PUT de ingress (H5): dos deploys simultáneos se borran la regla; en `Direct` = host caído sin aviso | Semáforo por túnel + relectura tras el PUT + fallo propagado. El defecto ya existe en el túnel central: deuda aparte |
| **ALTO A3** | Prefijos de slug (H12): una instancia `x` borra con `force` contenedores de `x-staging` y servicios gestionados `x-*` de la misma VM en cada deploy sano | Regla de nombres en la fase M. **Hallazgo**: en `principal` no queda ningún contenedor `ekippo-test-*` ni `ekippo-*-test` que F13.7 dejó "parados como rollback" (`docker ps -a --filter name=ekippo`, 2026-10-04) — compatible con que este defecto ya actuó, no probado (S11). Recomendado: OT aparte para que la limpieza excluya los slugs de las demás instancias de la VM |
| **ALTO A4** | Base de prod en Bogotá sin copia fuera de la VM hasta resolver P2 | Ningún cutover de InOneTickets a prod sin copia fuera de la VM verificada (gate de la fase M) |
| MEDIO | CNAME existente al túnel viejo `inonetickets`: con la semántica actual se saltaría y el host quedaría en 530/1033 | `DnsRecordRules` → `Conflict` explícito en `Direct` |
| MEDIO | Contexto de build git por SignalR a través del borde de Cloudflare, repo grande (S7) | Build git probado en el gate de F4 |
| MEDIO | Imágenes de ghcr privadas u obsoletas (H14, H21) | Modo git en el satélite (P3) |
| BAJO | Satélite caído → sin deploys (la app sigue sirviendo) | Monitor de estado de VM existente |
| BAJO | `InstanceRemoved` deja reglas de ingress y CNAMEs por servicio (ya pasa hoy en multi-host) | Deuda declarada |

## 5. Preguntas para el dueño

1. **P1 — dominio de staging.** La regla de la cookie (`docker-compose.staging.yml:40-45`) exige otro dominio registrable. ¿Cuál, y está su zona en la misma cuenta de Cloudflare?
2. **P2 — copia fuera de la VM de los backups de Bogotá.** (a) Tailscale en Bogotá → micro-2 (que ya sube a OCI inmutable; reutiliza OT-002) — **recomendado**; (b) bucket OCI propio de la tenancy de Bogotá.
3. **P3 — build de InOneTickets.** (a) modo git en el satélite (PAT read-only, como los siete hermanos) — **recomendado**; (b) reactivar el workflow de ghcr + autenticación de pull.

## 6. Supuestos a verificar en F0

S1 `cloudflare.tunnels` tiene una sola fila (`authorit-apps`, `TargetVmId=principal`) · S2 ningún `svc.Hostname` contiene `{` · S3 ninguna instancia en uso con `TargetVmId` ≠ principal · S4 Access bloquea `/api/satellite/binary` y los tarballs están en la imagen del central · S5 Bogotá sale por 443 al hub · S6 `docker.io` en Ubuntu 26.04 arm64 convive con el `REJECT` de Oracle sin afectar a Atril · S7 el contexto de build git de InOneTickets cabe por SignalR · S8 `BackupOrchestrator` hace visible el fallo con un servicio remoto · S9 estado de los records DNS de `inonetickets.lat` y del túnel viejo · S10 un túnel creado desde el dashboard es de config remota · S11 causa de la ausencia de `ekippo-test-*` · S12 hay vía para leer descifradas las credenciales admin de un servicio gestionado · nombre exacto del fichero de endpoints de `/api/cloudflare/tunnel/*`.
