# Auditoria y decision UX operacional: Git -> App Environment -> Maquina

Fecha: 2026-06-05

## Resumen ejecutivo

Aethra ya tiene muchas piezas correctas: proyectos, templates Git, clientes, instancias, builds, deployments, VMs con satelite, rutas YARP, Cloudflare, servicios gestionados, monitores, notas, API keys y MCP. El problema principal no es falta de capacidad. El problema es que la experiencia actual expone demasiado el modelo tecnico interno.

La premisa del producto debe ser:

> Un desarrollador sube un cambio a Git y quiere saber, sin navegar cinco pantallas, que app se afecto, en que cliente/ambiente quedo desplegada, en que maquina corre, que URL publica funciona, que fallo y que accion tomar.

Con esa premisa, la unidad mental principal no puede ser `Project`, `Template`, `Instance`, `Route`, `Build` ni `Deployment`. La unidad mental principal debe ser:

```text
App Environment = App + Tenant/Client + Environment
```

Un App Environment es "esta app, para este cliente, en este ambiente", con release actual, maquina, servicios, datos, URLs publicas, health e issues.

La recomendacion fuerte es:

- Mantener `Project` internamente, pero degradarlo en la experiencia a **Portfolio**.
- Mostrar `Template` como **App** o **App Definition**.
- Mostrar `Client` como **Tenant** cuando sea relevante.
- Mostrar `Instance` como **App Environment**.
- Consolidar `Build` + `Deployment` como **Release** desde la perspectiva del usuario.
- Reemplazar `/routes` por **Public Access**, agrupado por App Environment, hostname, estado y owner.

Esto no exige romper el modelo de datos en la primera fase. Puede empezar como read models y cambios de informacion. Pero si Aethra quiere escalar a muchas apps, muchos clientes y varios ambientes, la UI debe dejar de parecer una lista de entidades tecnicas.

## Decision principal: App Environment como centro

El caso que define la arquitectura de informacion es este:

- Una plantilla/repositorio representa una aplicacion.
- Esa aplicacion corre para dos clientes.
- Cada cliente tiene `dev`, `staging` y `production`.
- En el futuro pueden ser veinte clientes y mas ambientes.

Si la plataforma muestra eso como "un Template con muchas Instances", el desarrollador siente que esta administrando recursos internos. La vista correcta debe traducirlo a:

```text
App: Portal Clientes

               Dev              Staging          Production
Cliente A      healthy          healthy          failed
Cliente B      healthy          deploying        healthy
```

Cada celda de esa matriz es un App Environment:

```text
portal-clientes / cliente-a / production
portal-clientes / cliente-a / staging
portal-clientes / cliente-a / dev
portal-clientes / cliente-b / production
portal-clientes / cliente-b / staging
portal-clientes / cliente-b / dev
```

Esa organizacion contesta las preguntas reales:

- Que clientes tiene esta app?
- En que ambientes corre?
- Que ambiente esta roto?
- Que version corre en production por cliente?
- Que deploy acaba de afectar a staging?
- Que URL publica corresponde a cada ambiente?
- Que maquina sirve ese ambiente?
- Que clientes quedaron atrasados frente a una version?

## Que hacer con Project

No conviene eliminar `Project` en v1.

El modelo actual usa `Project` como agrupador de templates, clientes, instancias, variables, secretos, previews y metadata visual. Quitar `Project` ahora obligaria a redisenar muchas relaciones sin que el usuario gane algo inmediato. El problema no es la existencia de `Project`; el problema es que hoy compite con la unidad mental operativa.

La decision recomendada:

```text
Backend actual: Project
Nombre conceptual/UI: Portfolio
Rol: agrupador administrativo
Visibilidad: secundaria
```

`Project` debe conservarse para:

- Permisos y roles.
- Defaults compartidos.
- Variables y secretos de alto nivel.
- Clientes/tenants relacionados.
- Politicas de previews.
- Cuotas o limites.
- Agrupacion de apps relacionadas.
- Reportes y filtros administrativos.

Pero no debe ser el punto de entrada diario. El usuario operativo debe entrar por **Apps**, **App Environments**, **Releases**, **Public Access** o **Machines**.

En flujos simples, la plataforma puede auto-crear un Portfolio `Default` o derivarlo del nombre de la primera app. El usuario no deberia tener que entender Portfolio antes de desplegar.

## Modelo actual vs modelo mental recomendado

| Modelo actual | Rol real | Nombre recomendado en UX | Comentario |
| --- | --- | --- | --- |
| `Project` | Agrupa templates, clientes, instancias y configuracion | Portfolio | Mantener, pero sacar del centro operacional |
| `Template` | Define repo, build, servicios y automatizacion Git | App / App Definition | Es lo que el usuario reconoce como aplicacion |
| `Client` | Tenant o cliente funcional | Tenant / Client | Visible si la app es multi-tenant |
| `Instance` | Despliegue concreto de app + cliente + ambiente | App Environment | Unidad mental principal |
| `Build` | Construccion de artefacto | Parte de Release | No debe verse aislado del deploy |
| `Deployment` | Intento de correr una version en un ambiente | Parte de Release | Debe agruparse con build y verificacion |
| `Route` | Regla tecnica YARP | Detalle de Public Access | No debe ser la unidad principal |
| VM/Satellite | Maquina administrada | Machine | Debe mostrar readiness, no solo online/offline |

Jerarquia persistente actual:

```text
Project
  Template
  Client
  Instance
```

Jerarquia mental recomendada:

```text
Portfolio
  App
    App Environment
```

Dimensiones de un App Environment:

```text
App Environment =
  App
  + Tenant/Client
  + Environment
  + Machine
  + Release actual
  + Public Access
  + Services/Data
  + Config/Secrets
  + Operational Issues
```

## Como deberia sentirse para un desarrollador

Un desarrollador no deberia pensar:

> "Voy a Projects, abro un Template, busco Instances, luego miro Builds, luego Deployments, luego Routes para saber si quedo vivo."

Deberia pensar:

> "Voy a Apps, abro Portal Clientes y veo todos sus clientes y ambientes. Production de Cliente A esta fallando; abro la celda y veo release, logs, URL, maquina y accion."

La pantalla de una App debe tener tres modos segun volumen.

### Modo 1: app simple

Para una app sin multi-tenant:

```text
App: API Contabilidad

Environment     Status     Version     Machine       URL
production      healthy    a1b2c3      vm-prod-01    api.contabilidad.com
staging         healthy    d4e5f6      vm-stg-01     staging-api.contabilidad.com
preview/pr-42   expiring   9f8e7d      vm-preview    pr-42-api.aethra.dev
```

Tenant queda oculto como `default`.

### Modo 2: pocos clientes, varios ambientes

Para 2 a 10 clientes, una matriz es mas clara:

```text
App: Portal Clientes

               Dev              Staging          Production
Cliente A      ok               ok               failed
Cliente B      ok               deploying        ok
Cliente C      stale            ok               ok
```

Cada celda debe mostrar al menos:

- Health.
- Version o commit.
- Ultimo release.
- URL principal.
- Maquina.
- Issues.

Acciones rapidas por celda:

- Deploy.
- Rollback.
- Open URL.
- Logs.
- Public Access.
- Config.

### Modo 3: muchos clientes o muchos ambientes

Cuando la matriz crece demasiado, la UI debe cambiar a tabla filtrable:

| App Environment | Tenant | Environment | Status | Version | Machine | URL | Issues |
| --- | --- | --- | --- | --- | --- | --- | --- |
| portal / cliente-a / production | Cliente A | production | failed | a1b2c3 | vm-prod-01 | portal-a.com | 2 |
| portal / cliente-b / staging | Cliente B | staging | deploying | d4e5f6 | vm-stg-02 | staging-b.com | 0 |

Filtros obligatorios:

- Tenant.
- Environment.
- Status.
- Version/commit.
- Machine.
- Public endpoint.
- Has issues.
- Behind latest release.
- Preview/ephemeral.

Saved views recomendadas:

- Production failing.
- Staging deploying.
- Environments behind latest.
- Previews expiring.
- Missing public access.
- Same version by tenant.
- Same machine.

## Navegacion objetivo

La navegacion debe separar operacion diaria de configuracion.

### Operacion

- **Command Center**: bandeja de problemas, releases recientes, endpoints rotos y maquinas no listas.
- **Apps**: entrada principal por aplicacion; muestra clientes y ambientes.
- **App Environments**: lista global filtrable de todos los ambientes reales.
- **Releases**: cada push/manual trigger como unidad completa build + deploy + verificacion.
- **Public Access**: dominios, rutas, DNS, tunnel, TLS, monitor y health.
- **Machines**: VMs/satelites con capacidad real y workloads corriendo.
- **Data Services**: Postgres/Redis/etc., consumidores, backups y salud.
- **Operational Issues**: inbox accionable de fallos.

### Configuracion

- **Portfolios**: actual `Projects`.
- **App Definitions**: actual `Templates`, si se necesita editar repo/build/servicios.
- **Tenants**: actual `Clients`.
- **Environment Defaults**: variables, secretos, policies y defaults.
- **Domains / Cloudflare / Tunnel**: proveedores y configuracion base.
- **Integrations**: Git providers, notifications, MCP, API keys.
- **Settings**: usuarios, roles y plataforma.

## Plataforma definitiva autoalojada

Aethra deberia posicionarse como un control plane autoalojado para operar apps, maquinas, dominios y datos desde Git.

No debe copiar literalmente una herramienta. Debe combinar lo mejor de varias categorias:

- PaaS Git-first: deploy desde repos, webhooks, previews, rollback.
- Fleet management: agentes por maquina, readiness, workloads por VM.
- Public access reconciliation: host, path, DNS, tunnel, TLS, route y monitor como una sola intencion.
- Data services: servicios gestionados con consumidores, backups y restore.
- Observabilidad operacional: issues accionables, no solo metricas.
- MCP/API-first: agentes IA pueden explicar, desplegar y reconciliar con permisos auditables.

Frase objetivo:

> Esta app viene de este repo; production va en esta VM, staging en esta otra; cada push a main despliega production; cada PR crea preview; este dominio debe quedar publico; si algo falla, dime exactamente que falta y dame la accion segura para corregirlo.

La plataforma debe convertir esa intencion en estado deseado:

```yaml
app: portal-clientes
tenant: cliente-a
environment: production
source:
  repo: github.com/authorit/portal-clientes
  ref: main
target:
  machine: vm-prod-01
public_access:
  hostname: portal-a.example.com
  paths:
    /: frontend
    /api: backend
monitoring:
  enabled: true
data:
  postgres: portal_cliente_a
```

Luego reconciliadores internos aseguran:

- Build o reutilizacion de imagen.
- Deploy/swap de contenedores.
- Variables y secretos efectivos.
- Volumenes.
- Route.
- DNS.
- Tunnel ingress.
- TLS/edge.
- Monitor.
- Verificacion de endpoint.
- Issues si algo diverge.

## Public Access: reemplazo real de Routes

`/routes` es el ejemplo mas claro de por que el modelo actual no escala.

Una ruta YARP no es lo que el usuario quiere operar. El usuario quiere saber si una URL publica funciona y a que App Environment pertenece.

La pantalla debe llamarse **Public Access**.

Vista principal agrupada por hostname:

```text
portal-a.example.com
App: Portal Clientes    Tenant: Cliente A    Env: production    Machine: vm-prod-01
Health: Healthy         DNS: OK              Tunnel: OK          Monitor: Up
Source: Native deploy   Managed: Yes

Path      Service   Backend                         Status
/         frontend  http://portal-a-fe:5007         OK
/api      backend   http://portal-a-api:5006        OK
```

Filtros obligatorios:

- Search por hostname, path, backend, service, app, tenant o machine.
- Portfolio.
- App.
- Tenant.
- Environment.
- Machine.
- Source: native deploy, manual, MCP, migration, system.
- Type: custom domain, generated domain, preview.
- Status: healthy, degraded, broken, stale, unknown.
- DNS: ok, missing, wrong target.
- Tunnel: ok, missing, stale.
- Monitor: up, down, missing.
- TLS/cert: edge, issued, expiring, failed, disabled.

Acciones por host:

- Open URL.
- Verify endpoint.
- Reconcile.
- Ensure DNS.
- Ensure tunnel.
- Create monitor.
- Open latest release.
- Open App Environment.

Acciones por path:

- Copy backend.
- Verify backend.
- Open technical route.
- Delete route solo si es manual o stale confirmado.

Backend minimo:

- Mantener `Route`.
- Agregar metadata de owner/origen: app, tenant, environment, instance, machine, source, managed.
- Crear `GET /api/public-endpoints` paginado con filtros.

Backend recomendado:

- Crear `PublicEndpoint` como entidad o read model principal.
- Hacer que deploy nativo declare endpoints deseados.
- Reconciler garantiza Route + DNS + Tunnel + Monitor.
- Route queda como recurso tecnico generado.

## Releases: unir Builds y Deployments

Para el usuario, un push no es "un build" y luego "varios deployments". Es un intento de liberar una version.

La vista debe ser **Releases**:

```text
Git event -> Release -> Build artifact -> Deployments -> Containers -> Public endpoints -> Health checks -> Issues
```

Cada Release debe mostrar:

- Repo/ref/commit/autor.
- Trigger: webhook, manual, MCP, retry, rollback, preview.
- App.
- Tenants/environments afectados.
- Build status.
- Deploy fan-out.
- Endpoints verificados.
- Duracion.
- Resultado agregado.
- Issues generados.

El detalle debe tener timeline:

```text
Webhook received
Build started
Image pushed
Deploy cliente-a/staging
Deploy cliente-b/staging
Verify public endpoints
Create issues
Finished
```

Acciones:

- Retry failed.
- Redeploy selected App Environments.
- Rollback.
- Open logs.
- Open affected endpoint.
- Compare versions.

## Machines: readiness, no solo online/offline

Una VM online puede no estar lista para desplegar. La plataforma debe mostrar capacidad real.

`MachineCapability` debe responder:

- El satelite esta conectado?
- La version del satelite coincide?
- Docker/Podman responde?
- Puede construir imagenes?
- Puede correr contenedores?
- Puede leer logs?
- Existe la red esperada?
- Hay espacio en disco?
- Tiene presion de CPU/RAM?
- Acepta previews?
- Que App Environments corren ahi?

Estados recomendados:

- Ready.
- Degraded.
- Not ready.
- Offline.
- Unknown.

El usuario debe poder filtrar App Environments por maquina y ver si un fallo de deploy viene de la app o de la capacidad de la VM.

## Data Services y configuracion efectiva

Si Aethra quiere ser plataforma definitiva autoalojada, Postgres, Redis y otros servicios no deben ser listas tecnicas aisladas.

Cada Data Service debe mostrar:

- Consumidores: Apps y App Environments que lo usan.
- Backups.
- Restore drills.
- Retention.
- Storage.
- Health.
- Credenciales y rotacion.
- Clones para previews.
- Migraciones recientes.

Cada App Environment debe tener una vista de **Config & Secrets** que responda:

- De donde sale esta variable?
- Portfolio, App, Tenant, App Environment, service binding o secret?
- Que valor gana por precedencia?
- Que cambio desde el ultimo release?
- Requiere rebuild, redeploy o solo restart?
- Que secrets fueron rotados?

Esto reduce errores porque muchos fallos de deploy son problemas de configuracion efectiva, no de Git.

Estado de avance:

- `GET /api/ops/data-services` ya muestra servicios gestionados y consumidores por App Environment.
- `/instances/{id}` ya muestra los Data Services consumidos por ese App Environment.
- `GET /api/ops/app-environments/{appEnvironmentId}/effective-config` ya resuelve la precedencia `Instance > Client > Template > Project`.
- La pestaña Config de `/instances/{id}` ya muestra un inspector efectivo con variables, secretos enmascarados, scope ganador, uso build/runtime, overrides ocultos y drift frente al ultimo deploy exitoso.
- Cuando hay drift, el inspector ya ofrece accion directa de deploy/redeploy desde la misma tarjeta.
- `/operational-issues` ya genera `config.changed_since_last_deploy` cuando config efectiva cambio despues del ultimo deploy exitoso.
- Pendiente: detectar rotacion semantica de secretos por binding y diferenciar redeploy vs restart.

## Operational Issues

Los problemas no deben estar escondidos en rutas, monitores, deployments, VMs y Cloudflare. Deben aparecer en una sola bandeja accionable.

Ejemplos:

- `endpoint.dns_missing`: host sin DNS.
- `endpoint.tunnel_missing`: DNS existe pero tunnel/ingress falta.
- `endpoint.backend_unreachable`: route apunta a contenedor muerto.
- `endpoint.monitor_missing`: host publico sin monitor.
- `route.owner_missing`: ruta sin App Environment.
- `machine.satellite_offline`: VM sin satelite.
- `machine.not_ready`: VM online pero sin runtime disponible.
- `release.build_failed`: build fallo.
- `release.deploy_failed`: deploy fallo.
- `config.key_type_conflict`: la misma key gana como variable y como secreto en un App Environment.
- `config.changed_since_last_deploy`: variable o secreto efectivo cambio pero el ambiente no se ha redeployado con esa config.
- `preview.expired`: preview debe limpiarse.

Cada issue debe tener:

- Severity.
- Owner.
- App Environment.
- First seen / last seen.
- Causa probable.
- Accion sugerida.
- Boton: verify, reconcile, retry, open logs, create monitor, ensure DNS, snooze.

## MCP y API como operadores de alto nivel

El MCP no debe limitarse a CRUD tecnico. Debe operar los mismos conceptos de alto nivel que la UI:

- `aethra_explain_app_environment_status`
- `aethra_deploy_app_environment`
- `aethra_trace_release`
- `aethra_find_broken_public_endpoints`
- `aethra_reconcile_public_endpoint`
- `aethra_create_preview_environment`
- `aethra_compare_environment_versions`
- `aethra_rotate_service_credentials`

Cada accion debe tener:

- Scopes.
- Audit log.
- Dry-run cuando sea destructiva o amplia.
- Resultado explicable.

## Busqueda global y command palette

Buscar `portal cliente-a prod` debe encontrar:

- App.
- App Environment.
- Public endpoint.
- Ultimo Release.
- Machine.
- Data Services.
- Operational Issues.
- Notes.

El usuario no debe recordar en que modulo vive cada cosa.

La command palette debe permitir:

- Deploy app environment.
- Open logs.
- Open public URL.
- Verify endpoint.
- Retry release.
- Rollback.
- Create preview.
- Filter broken production endpoints.

## Evaluacion por funcionalidad actual

| Funcionalidad actual | Problema | Reorganizacion recomendada | Prioridad |
| --- | --- | --- | --- |
| Dashboard | Demasiado indice/KPI, poca accion | Command Center con issues, releases recientes y endpoints rotos | P1 |
| Projects | Agrupador util, pero demasiado protagonista | Renombrar a Portfolio y mover a configuracion | P1 |
| Templates | Nombre tecnico para una app/repositorio | Mostrar como Apps o App Definitions | P1 |
| Clients | Necesario para multi-tenant, ruidoso en apps simples | Mostrar como Tenants solo cuando aplique | P1 |
| Instances | Es la unidad real, pero mal nombrada | Mostrar como App Environments | P0 |
| Builds | Lista separada y fan-out de datos | Integrar en Releases con endpoint global paginado | P0/P1 |
| Deployments | Mejor contexto, pero separado del build | Integrar en Releases y App Environment detail | P1 |
| Routes | Tabla tecnica plana, no escala | Reemplazar por Public Access agrupado por hostname/owner | P0 |
| VMs/Satellite | Online/offline no basta | Machines con readiness y capability snapshot | P1 |
| Services | Lista tecnica de servicios | Data Services con consumidores, backups y health | P2 |
| Cloudflare/Tunnel | Proveedor separado del resultado publico | Integrar estado en Public Access; dejar proveedor en config | P1 |
| Monitors | Buen punto de partida, pero aislado | Asociar a PublicEndpoint y App Environment | P2 |
| Notes/Facts | Utiles, pero perifericos | Contextuales por App, Machine, Release e Issue | P2 |
| API Keys/MCP | Potente pero CRUD-oriented | Presets y acciones por App Environment, Release, PublicEndpoint | P2 |

## APIs y read models recomendados

No todos los conceptos deben nacer como entidades transaccionales. La primera fase puede usar read models.

Endpoints recomendados:

```text
GET /api/ops/apps
GET /api/ops/app-environments
GET /api/ops/releases
GET /api/ops/releases/{releaseId}
GET /api/ops/public-endpoints
GET /api/ops/machines
GET /api/ops/operational-issues
```

Compatibilidad:

- Mantener endpoints actuales de projects/templates/clients/instances.
- Mantener endpoints tecnicos de builds/deployments/routes.
- Usar los nuevos endpoints para vistas operacionales.

Read models:

- `AppOverview`.
- `AppEnvironmentOverview`.
- `ReleaseOverview`.
- `PublicEndpointOverview`.
- `MachineOverview`.
- `OperationalIssue`.

Campos minimos de `AppEnvironmentOverview`:

- `appEnvironmentId` o `instanceId`.
- `portfolioId`, `portfolioName`.
- `appId`, `appName`, `repoUrl`.
- `tenantId`, `tenantName`, opcional.
- `environment`.
- `machineId`, `machineName`, `machineStatus`.
- `trackedRef`, `currentGitSha`.
- `latestReleaseId`, `latestReleaseStatus`, `latestReleaseAt`.
- `publicUrl`, `publicEndpointStatus`.
- `serviceCount`, `dataServiceCount`.
- `issueCount`, `highestSeverity`.
- `healthStatus`.

## Fases de implementacion recomendadas

Estado actual de implementacion:

- `GET /api/ops/apps`, `/api/ops/app-environments`, `/api/ops/releases`, `/api/ops/public-endpoints`, `/api/ops/machines` y `/api/ops/operational-issues` existen como read models host-level.
- `/apps` ya es la entrada operacional para aplicaciones, con detalle y matriz Tenant x Environment cuando el volumen lo permite.
- `/app-environments`, `/releases`, `/public-access`, `/operational-issues` y `/vms` ya consumen la capa operacional.
- `/instances/{id}` ya actua como detalle de App Environment: conserva tabs tecnicos, pero arriba muestra salud, release actual, public access, machine e issues.
- `/releases/{id}` ya actua como detalle operacional de Release: muestra Git/ref/SHA, artefacto, timeline, fan-out por App Environment, public access, machine, issues y enlaces tecnicos a build/deployment.
- `/app-environments` ya soporta filtros server-side por busqueda, status, app, environment y machine.
- `/releases` ya soporta filtros server-side por busqueda, status, app y Git ref.
- `/public-access` ya soporta filtros server-side por busqueda, health, app, environment, DNS, Tunnel y Monitor.
- `GET /api/ops/public-access-states` ya expone estado deseado vs estado real de Public Access por App Environment.
- `/instances/{id}` ya muestra checklist de Public Access con desired hostname, DNS, Tunnel, Route, TLS, Monitor, issues y siguiente accion.
- `POST /api/ops/public-access-states/{appEnvironmentId}/reconcile` ya permite dry-run y reconciliacion operacional de DNS, Tunnel, Route/TLS y Monitor para el hostname deseado.
- `/instances/{id}` ya permite ejecutar `Dry run` y `Reconcile` desde la tarjeta de Public Access.
- `/public-access` ya expone `Dry run` y `Reconcile` por endpoint con owner operacional resuelto, para que una lista grande de hostnames sea accionable sin abrir pantallas tecnicas de Routes.
- `/public-access` y `/instances/{id}` ya exponen `Verify` para comprobar rutas publicas por `PathPrefix`, backends deduplicados y Monitor manual en una sola accion operacional.
- `/operational-issues` ya soporta filtros server-side por busqueda, severidad, tipo de recurso y app.
- `/operational-issues` ya muestra accion sugerida y destino operacional por issue, para saltar a App Environment, Build o Public Access sin interpretar codigos tecnicos.
- `/operational-issues` ya tiene quick filters para Critical, Public Access, Machines y Config drift.
- `/operational-issues` ya genera `config.key_type_conflict` cuando una key efectiva existe como env var y secret en el mismo App Environment.
- `/operational-issues` ya genera `config.changed_since_last_deploy` cuando variables o secretos efectivos cambiaron despues del ultimo deployment exitoso.
- `/vms`/Machines ya soporta filtros server-side por busqueda, readiness y preview pool.
- `/vms`, `/dashboard` y `/instances/{id}` ya muestran `readinessReason`, no solo el estado de la machine.
- `/operational-issues` ya genera `machine.not_ready` para machines offline o con estado desconocido.
- El handshake del Satellite ya reporta runtime de contenedores y disco raiz; `MachineOverview` muestra runtime, memoria, disco raiz libre y degrada readiness cuando el runtime es desconocido o queda menos de 10% de disco libre.
- `GET /api/ops/data-services` ya existe como read model operacional de servicios gestionados y bindings por App Environment.
- `/instances/{id}` ya muestra los Data Services consumidos por ese App Environment.
- `/data-services` ya existe como vista operacional filtrable por busqueda, status y tipo, y `/services` queda como vista tecnica.
- `/dashboard` ya funciona como Command Center con issues, releases, public access y machines.
- `GET /api/ops/search` y `/search` ya permiten busqueda global de Apps, App Environments, Releases, Public Endpoints, Machines y Data Services.
- El topbar ya incluye Command Palette con `Ctrl/Cmd+K` y `/`, reutilizando `GET /api/ops/search` para saltar desde cualquier pantalla a Apps, App Environments, Releases, Public Endpoints, Machines y Data Services.
- `Route` ya persiste `operational_owner_type`, `operational_owner_id` y `origin`; Public Access prefiere ese owner antes de inferirlo por hostname/backend y marca `route_owner_mismatch` cuando el hostname apunta a otro App Environment.
- `/projects`, `/templates`, `/clients` y `/routes` quedan como compatibilidad/configuracion tecnica, no como camino principal.

### Fase 1: Reencuadre sin romper modelo

- Renombrar en UI:
  - Project -> Portfolio.
  - Template -> App/App Definition.
  - Instance -> App Environment.
  - Route -> Public Access.
- Crear read model `AppEnvironmentOverview`.
- Crear `/apps` como entrada principal.
- Crear vista de App con matriz Tenant x Environment.
- Ocultar Tenant cuando sea `default` o no aplique.

### Fase 2: Public Access

- Crear `GET /api/ops/public-endpoints`.
- Redisenar `/routes` como `/public-access`.
- Agrupar por hostname y resolver owner operacional cuando el backend permite asociarlo a un App Environment.
- Filtrar por busqueda, health, app, environment, DNS, Tunnel y Monitor.
- Mostrar DNS target vs target esperado, tunnel, TLS, Monitor, rutas tecnicas y issues.
- Verificar DNS, tunnel, route, backend, path publico y monitor.
- Generar issues por faltantes y llevar al usuario al App Environment o Public Access correcto.

Estado de avance:

- `/public-access` ya existe como reemplazo operacional de `/routes`.
- `GET /api/ops/public-endpoints` ya agrupa por hostname e incluye owner, DNS, Tunnel, TLS, Monitor, health, issues y rutas.
- `GET /api/ops/public-access-states` ya calcula estado deseado vs real por App Environment.
- `POST /api/ops/public-access-states/{appEnvironmentId}/reconcile` ya repara DNS, Tunnel ingress, Route/TLS y Monitor cuando hay suficiente configuracion.
- `POST /api/ops/public-access-states/{appEnvironmentId}/verify` ya valida cada path publico expuesto por YARP, cada backend unico y el Monitor asociado; los checks devuelven `label` y `target` para que el fallo sea localizable.
- `Route` ya persiste owner/origen operacional. InstanceProvisioned, DeploymentCompleted, NativeDeploy, MCP attach-domain y Public Access Reconcile escriben `app_environment:{id}` con origen del flujo; rutas manuales quedan como `manual` o `unknown`.
- Pendiente: backfill de owner/origen para rutas historicas existentes antes de esta migracion.

### Fase 3: Releases

- Crear `ReleaseOverview` o `ReleaseRun`.
- Unificar build + deploy + verificacion en una vista.
- Eliminar fan-out pesado del frontend para builds.
- Mostrar timeline por commit/push/manual trigger.
- Agregar retry failed, rollback y redeploy selected.

Estado de avance:

- `ReleaseOverview` existe en `/api/ops/releases`.
- `/releases` lista releases como unidad build + deploy fan-out.
- `/releases/{id}` muestra detalle operacional y mantiene links a `/builds/{id}` y `/deployments/{id}` como soporte tecnico.
- `/releases/{id}` permite `Retry`/`Redeploy` por App Environment reutilizando `POST /api/deployments/builds/{buildId}/instances/{instanceId}/trigger`.
- `POST /api/deployments/{deploymentId}/rollback` ya encola un deployment nuevo hacia el mismo App Environment reutilizando build/image de un deployment `Completed`.
- `/releases/{id}` ya muestra `Rollback` para targets completados, ademas de `Retry`/`Redeploy`.
- Pendiente: selector asistido de rollback desde App Environment detail, comparando version actual vs versiones historicas antes de confirmar.

### Fase 4: Machines y readiness

- Crear `MachineOverview` y evolucionarlo luego a `MachineCapabilitySnapshot` si se necesita capacidad historica o calculos de scheduling.
- Mostrar readiness en Machines y App Environment.
- Bloquear o advertir deploys cuando la maquina no este lista.
- Crear issues de VM/satelite/runtime.

Estado de avance:

- `GET /api/ops/machines` ya expone `readinessStatus` y `readinessReason`.
- La razon se muestra en Machines, Dashboard y App Environment detail.
- Operational Issues ya crea `machine.not_ready` para machines offline/unknown.
- El Satellite ya envia `containerRuntime`, `rootDiskTotalBytes` y `rootDiskAvailableBytes` en handshake; VMs persiste esos campos y Machines los usa para diferenciar not-ready de capacidad degradada.
- Pendiente: ampliar capability snapshot con version real de Docker/Podman, espacio por volumen de datos y checks de permisos del socket/runtime.

### Fase 5: Reconciliation y estado deseado

- Declarar public access desde App Environment.
- Reconciler crea/actualiza Route, DNS, Tunnel y Monitor.
- Agregar dry-run para cambios amplios.
- Mostrar drift entre estado deseado y estado actual.

Estado de avance:

- El estado deseado se declara desde App Environment mediante `customDomain` o `autoHostname`.
- `SetCustomDomainCommand` ya emite eventos de dominio custom para Cloudflare/Proxy.
- `PublicAccessState` muestra drift operativo para DNS, Tunnel, Route, TLS y Monitor.
- `POST /api/ops/public-access-states/{appEnvironmentId}/reconcile` ya permite `dryRun` y aplica acciones reconciliables desde la unidad mental correcta.
- El reconciler actual crea o repara CNAME proxied hacia `NativeDeploy:TunnelCname` cuando hay zona Cloudflare registrada.
- El reconciler actual asegura ingress en Cloudflare Tunnel remoto mediante `EnsureTunnelHostnameCommand` cuando hay tunnel gestionado registrado.
- El reconciler actual crea Route faltante, actualiza backend/TLS de Route existente, crea Monitor faltante y dispara check manual si el Monitor esta `Down`.
- El reconciler actual corrige tambien owner/origen de Route cuando el hostname deseado existe pero su metadata apunta a otro App Environment.
- Si falta hostname, zona DNS, tunnel gestionado, CNAME esperado o puerto primario, la operacion devuelve accion `blocked` para ese tramo en vez de asumir configuracion insegura.
- La UI de App Environment ya muestra botones `Dry run` y `Reconcile` en Public Access, con checks DNS/Tunnel/Route/TLS/Monitor.
- La UI global de Public Access ya muestra las mismas acciones por hostname cuando el owner apunta a un App Environment, ademas de DNS target, target esperado y tunnel.
- `POST /api/ops/public-access-states/{appEnvironmentId}/verify` ya ejecuta verificacion manual por cada `PathPrefix` publico, cada backend unico y Monitor cuando existe.
- Pendiente: incorporar certificados/edge TLS, politicas de reconciliacion por ambiente y metadata de owner/origen persistida al mismo reconciler de alto nivel.

Contrato actual:

```text
GET  /api/ops/public-access-states?appEnvironmentId={id}
POST /api/ops/public-access-states/{appEnvironmentId}/reconcile
POST /api/ops/public-access-states/{appEnvironmentId}/verify
```

Payload:

```json
{ "dryRun": true }
```

Resultado:

```json
{
  "appEnvironmentId": "inst_x",
  "dryRun": true,
  "applied": false,
  "actions": [
    {
      "kind": "create_route",
      "status": "planned",
      "message": "Crear Route host -> backend.",
      "resourceId": null,
      "errorCode": null,
      "errorMessage": null
    }
  ],
  "state": {}
}
```

### Fase 6: Operacion avanzada

- Data Services con consumidores y backups.
- Config & Secrets inspector con drift frente al ultimo deploy exitoso.
- Search global.
- Command palette.
- MCP de alto nivel.
- Policies simples.
- Quick filters/Saved views para listas operacionales.

Estado de avance:

- Search global ya existe como endpoint operacional y pagina `/search`.
- Command Palette ya existe como modal desde el topbar, con atajos `Ctrl/Cmd+K` y `/`, busqueda incremental y navegacion directa al recurso operacional.
- Pendiente: convertir la Command Palette de buscador/navegador a operador de alto nivel: deploy, verify endpoint, retry release, rollback, open logs y create preview desde resultados contextualizados.

## Backlog priorizado

### P0

- Definir `App Environment` como unidad mental y renombrar `Instance` en UI.
- Rehacer `/routes` como `Public Access` o al menos agregar owner/origen.
- Crear read model global de App Environments con filtros server-side.
- Evitar que `/builds` dependa de fan-out desde frontend.

### P1

- App detail con matriz Tenant x Environment.
- Command Center con issues accionables.
- Releases como build + deploy + verificacion.
- Machine readiness.
- Public endpoint verification.
- Filtros server-side para listas operacionales de alto volumen.

### P2

- Data Services con consumidores.
- Config & Secrets drift/redeploy guidance.
- Notes/Facts contextuales.
- MCP actions de alto nivel.
- Saved views persistentes para listas grandes.

### P3

- Renombres finales de rutas y textos.
- Exportar desired state.
- Backup/restore del control plane.
- Disaster recovery runbook.

## Criterios de aceptacion

- Un desarrollador abre una App y entiende en menos de 10 segundos que clientes y ambientes estan sanos o rotos.
- Una app con 2 clientes y 3 ambientes se ve como matriz, no como lista tecnica de 6 instancias.
- Una app con muchos clientes se puede filtrar por tenant, environment, status, version, machine y public endpoint.
- Un App Environment muestra release actual, commit, maquina, URLs, servicios, datos, config e issues.
- Config de App Environment muestra que variables y secretos aplican realmente y que scope gana por precedencia.
- Una ruta publica siempre muestra su App Environment owner, origen, estado DNS, tunnel, route, TLS y monitor.
- Un push a Git se ve como un Release unico con build, deploy fan-out y verificacion.
- Una VM online pero incapaz de desplegar aparece como "not ready" con razon concreta.
- El usuario no necesita entender YARP Route, Cloudflare DNS, Tunnel ingress y Monitor para saber si su URL publica funciona.
- `Project` deja de ser centro operacional y pasa a Portfolio/configuracion.
- El flujo simple permite crear una app sin obligar al usuario a tomar decisiones sobre Project/Client si no aplica.

## Fuentes consultadas

- Coolify Applications: https://coolify.io/docs/applications/index
- Dokploy Applications: https://docs.dokploy.com/docs/core/applications
- CapRover Getting Started: https://caprover.com/docs/get-started.html
- CapRover Deployment Methods: https://caprover.com/docs/deployment-methods.html
- Portainer Edge Stacks: https://docs.portainer.io/user/edge/stacks
- Vercel Git Deployments: https://vercel.com/docs/git
- Render Preview Environments: https://render.com/docs/preview-environments
- Railway Environments: https://docs.railway.com/environments
