# Aethra

**Una sola plataforma para desplegar, enrutar, certificar, monitorear y operar tu infraestructura.**

Aethra unifica en un único sistema —con una base de datos compartida y una sola UI— lo que hoy te obliga a saltar entre cuatro herramientas distintas: **despliegue Git→Docker** (en lugar de Coolify), **reverse proxy + TLS automático** (en lugar de Traefik), **monitoreo de uptime** (en lugar de Uptime Kuma) y **métricas de VMs y contenedores** (en lugar de Beszel). El proyecto, la URL pública, las variables de entorno, el monitor que la vigila y la nota con sus credenciales viven en el mismo lugar — no en cuatro lugares que nadie sincroniza.

Multi-tenant nativo: una `Template` (un repo Git) puede correr para N clientes (`Client`) en M ambientes (`Instance`), cada uno con sus propias variables, secretos, dominio y deploy independiente. Una sola imagen se construye y se despliega a todos los clientes que la usan — sin duplicar configuración.

API-first y operable por humanos o por agentes IA: el servidor MCP embebido (Model Context Protocol) expone las operaciones críticas como herramientas tipadas que Claude o cualquier agente compatible puede invocar para crear proyectos, dispararse deploys, leer métricas y dejar notas.

---

## Qué hace por dentro

| Capacidad | Cómo funciona |
|---|---|
| **Despliegue Git→Docker** | Webhook firmado HMAC dispara `Build` (clone shallow → docker/podman build → push al registry interno). Al completar, fan-out a N `Deployment` (pull → run → healthcheck → atomic swap de la ruta YARP). 1 Build, N Deployments. |
| **Reverse proxy + TLS** | YARP embebido en el proceso central. Las rutas viven en BD; al cambiar, `IProxyConfigService.Reload()` actualiza YARP en caliente. Let's Encrypt vía Certes con renovación automática (worker cada 1h, ventana configurable). |
| **Multi-tenant** | Auto-hostname `{template}-{client}-{env}.{base-domain}` al crear Instance. Custom domain opcional con CNAME Cloudflare. Variables y secretos se resuelven cascada `Instance > Client > Template > Project`. |
| **Monitoreo** | `MonitorWorker` ejecuta probes HTTP con tick configurable; cada probe en scope propio para no bloquearse. Cambios de estado emiten integration events que llegan a SignalR (UI live) y a la línea de tiempo del proyecto. |
| **Métricas VM + Docker** | Satélite ligero (.NET) conecta al central por SignalR (WebSocket persistente — solo egress 443 saliente, sin abrir puertos). Reporta CPU/RAM/disco/red del SO + stats por contenedor. Buffer SQLite local mientras la red falle, drena al reconectar. |
| **Servicios gestionados** | Postgres/Redis/RabbitMQ one-click vía plantillas. `ServiceBinding` provisiona BD/user/password reales y los inyecta como env vars + secrets en las apps que los usan. |
| **DNS Cloudflare** | Cliente HTTP contra API v4. Registros A/CNAME automáticos al adjuntar custom domain. Token cifrado en Settings, referenciado por nombre. |
| **Notas y PinnedFacts** | Markdown + imágenes por proyecto/template/instance. Los PinnedFacts (IPs, credenciales, comandos) se muestran en la tarjeta principal y se cifran en reposo. |
| **API + MCP** | REST con OpenAPI 3.1, auth dual (cookie para humanos, API keys con scopes granulares para clientes). Servidor MCP embebido en `wss://aethra/mcp` con tools tipadas. |
| **Operación por agentes IA** | API keys con scopes (`projects:read`, `deployments:write`, ...). Cada endpoint REST exige el scope correspondiente; endpoints sensibles (gestión de API keys, secrets) son cookie-only. |

---

## Arquitectura

```
┌────────────────────────────────────────────────────────────────┐
│  VM-Central                                                    │
│  ┌──────────────┐   ┌──────────────────────────────────────┐   │
│  │ apps/web     │   │ apps/api                             │   │
│  │ Next.js 16   │◄──┤ ASP.NET Core (.NET 10)               │   │
│  │ App Router   │   │  • YARP (reverse proxy + TLS)        │   │
│  └──────────────┘   │  • SignalR Hub (satellite + UI)      │   │
│                     │  • MCP server (tools para agentes)   │   │
│                     │  • Background workers                 │   │
│                     │       Build, Deployment, Monitor,     │   │
│                     │       CertRenewal, OutboxDispatchers  │   │
│                     │       (12: uno por DbContext)         │   │
│                     └──────────────────────────────────────┘   │
│                                                                │
│   ┌──────────────┐    ┌────────────────┐    ┌──────────────┐  │
│   │ PostgreSQL   │    │ Docker daemon  │    │ Registry     │  │
│   │ (12 schemas, │    │ (local builds  │    │ interno      │  │
│   │  1 por módulo)│    │  y servicios)  │    │ (registry:2) │  │
│   └──────────────┘    └────────────────┘    └──────────────┘  │
└────────────────────────────▲───────────────────────────────────┘
                             │ SignalR (wss, egress only)
            ┌────────────────┴─────────┬─────────────────────┐
            │ VM-Satellite 1            │ VM-Satellite N      │
            │ apps/satellite (.NET)     │ ...                 │
            │ IContainerRuntime         │                     │
            │  ├─ DockerContainerRt     │                     │
            │  └─ PodmanContainerRt     │                     │
            │ Métricas SO + contenedores                      │
            └───────────────────────────┴─────────────────────┘
```

**Modular monolith con fronteras estrictas.** Cada `Modules.<X>` es un bounded context con su propio schema PostgreSQL, su propio DbContext, sus aggregates y su outbox local. La comunicación cross-module es exclusivamente vía `IIntegrationEvent` en `Aethra.Shared.Contracts` — nunca por referencia directa. Las violaciones las detecta `tests/Aethra.ArchitectureTests` con NetArchTest (no se mergea código que cruce módulos por la puerta de atrás).

**Por qué SignalR y no agentes pull.** Beszel (WebSocket+CBOR) y Netdata (HTTP streaming replication) — las dos referencias más cercanas — usan push iniciado por el agente sobre conexión persistente. Razones: en Oracle Cloud y similares, los satélites están detrás de firewalls; push solo necesita egress 443. Bidireccionalidad gratis: el central puede mandar comandos al satélite por el mismo socket (build, run, stream logs). SignalR es el equivalente .NET nativo: reconexión automática con backoff, heartbeats, streaming.

---

## Stack

- **Backend**: .NET 10, ASP.NET Core, EF Core 10, YARP, SignalR, MediatR, FluentValidation, Polly, Docker.DotNet, Certes (ACME/Let's Encrypt), `ModelContextProtocol` SDK.
- **Frontend**: Next.js 16 (App Router), TypeScript, Tailwind, `@microsoft/signalr`.
- **BD**: PostgreSQL 16 (12 schemas, uno por bounded context — `projects`, `deployments`, `proxy`, `monitoring`, ...).
- **Secretos en reposo**: ASP.NET Data Protection con purposes por dominio (`aethra-integration-creds`, `aethra-webhook-secrets`, `aethra-cert-pfx`, `aethra-secrets-store`, ...).
- **Tests**: NetArchTest (fences arquitectónicas), xUnit (handlers), Testcontainers para integración.

---

## Cómo arrancar

### Instalación express

Si tienes `bash`, `dotnet 10 SDK`, `psql` y `node 24+` en el PATH:

```bash
./install.sh
```

El script verifica prerequisitos, crea la BD `aethra`, aplica migraciones, hashea la password admin, copia un `appsettings.Local.json` mínimo y arranca central + frontend.

### Manual

```bash
# 1. Postgres en local (o usa el compose en deploy/)
createdb -U postgres aethra

# 2. Set the admin password seed (override the dev default)
export Identity__AdminEmail="tu@correo.com"
export Identity__AdminPasswordSeed="tu-clave-segura"

# 3. Central (escucha en http://localhost:5000)
dotnet run --project apps/api

# 4. Frontend (escucha en http://localhost:3000)
cd apps/web && npm install && npm run dev

# 5. (Opcional) Satélite local conectado al central
dotnet run --project apps/satellite
```

Credenciales por defecto en desarrollo (si no setteas `Identity__*`): `admin@aethra.local` / `aethra-dev`. **Cambia esto antes de exponer al exterior.**

---

## Estructura del repo

```
apps/
  api/         ASP.NET Core (servidor central)
  web/         Next.js 16 (UI)
  satellite/   ASP.NET Core ligero (agente por VM)
src/
  Aethra.Shared.Kernel/         primitivos: Result<T>, AethraId, IClock
  Aethra.Shared.Contracts/      eventos de integración + interfaces cross-module
  Aethra.Shared.Infrastructure/ pipelines MediatR, outbox base, persistencia compartida
  Aethra.Modules.Projects/      Project, Template, Client, Instance, EnvVars, Secrets
  Aethra.Modules.Deployments/   Build, Deployment, Webhooks, orchestrators
  Aethra.Modules.Services/      ManagedService, ServiceBinding, provisioners
  Aethra.Modules.Proxy/         Routes (YARP), Certificates (ACME)
  Aethra.Modules.Vms/           Vm, SatelliteHub, registry
  Aethra.Modules.Metrics/       VmMetric, ContainerMetric
  Aethra.Modules.Monitoring/    Monitor, MonitorCheck
  Aethra.Modules.Cloudflare/    Zone, DnsRecord
  Aethra.Modules.Notes/         Note, PinnedFact, imágenes
  Aethra.Modules.Identity/      User, ApiKey, scopes
  Aethra.Modules.Settings/      IntegrationCredential, BaseDomain, EnvironmentDefinition
  Aethra.Modules.Mcp/           Servidor MCP + tools para agentes
tests/
  Aethra.ArchitectureTests/     NetArchTest fences
scripts/
  smoke-c.sh                    Handshake E2E central↔satellite
  migrate-from-coolify.{sh,ps1} Migración asistida desde Coolify
deploy/
  docker-compose.yml + Dockerfiles
docs/
```

---

## API y MCP

REST completo con OpenAPI 3.1 generado en `/openapi/v1.json`. Las operaciones críticas se exponen también como tools MCP en `wss://aethra/mcp`:

- `aethra_list_context` — snapshot agregado (proyectos, VMs, servicios, dominios).
- `aethra_create_template` / `aethra_create_client` / `aethra_create_instance`.
- `aethra_trigger_build`, `aethra_trigger_deployment`.
- `aethra_attach_domain`, `aethra_set_env_vars`, `aethra_set_secrets`.
- `aethra_bind_service` (provisiona Postgres/Redis/RabbitMQ + inyecta credenciales).
- `aethra_query_metrics`, `aethra_get_monitor_status`, `aethra_add_note`.

Las respuestas incluyen `next_actions: [{ tool, why, suggested_args }]` para que el agente sepa qué proponer después sin tener que adivinar el modelo de datos.

---

## Seguridad

- Auth dual: cookie HttpOnly para UI humana, API keys con scopes granulares para agentes y clientes externos.
- Cada endpoint REST exige scope `<resource>:<read|write>`. La cookie equivale a admin; las API keys solo a sus scopes declarados.
- Endpoints sensibles (`/auth/me`, `/auth/logout`, gestión de API keys, integraciones de Settings) son **cookie-only** — bloquea bypass desde una API key.
- HMAC SHA-256 con `CryptographicOperations.FixedTimeEquals` en webhooks Git, body raw.
- Webhook secrets, integration credentials, PinnedFacts y service binding passwords cifrados en reposo con Data Protection (purposes separados para que un compromiso no exponga el resto).
- TLS automático Let's Encrypt; HTTP-01 servido por el propio YARP.
- Tests de arquitectura (NetArchTest) impiden que el Domain referencie EF/ASP.NET, y que un módulo importe internals de otro.

---

## Licencia

Privado.
