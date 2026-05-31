# Aethra

Plataforma unificada de despliegue, monitoreo y operación para infraestructura personal.
Reemplaza Coolify + Traefik + Uptime Kuma + Beszel con una sola UI, una sola base de datos
y una API automatizable por agentes IA (MCP).

## Estado

🚧 **Fase F0 — Andamiaje + esqueleto modular** (en curso).

Ver el plan completo y roadmap en
`C:\Users\johan.valencia\.claude\plans\tengo-3-vm-en-peaceful-raccoon.md`.

## Stack

- **Backend:** .NET 10, ASP.NET Core, EF Core, YARP, SignalR
- **Frontend:** Next.js (última versión estable, App Router), TypeScript, Tailwind
- **Base de datos:** PostgreSQL 16
- **Patrones:** monolito modular, CQRS con MediatR, eventos de dominio + Outbox

## Estructura

```
apps/
  api/         ASP.NET Core (servidor central)
  web/         Next.js (UI)
  satellite/   ASP.NET Core ligero (agente por VM)
src/
  Aethra.Shared.Kernel/         primitivos: Result<T>, DomainEvent, IClock
  Aethra.Shared.Contracts/      eventos de integración cross-module (DTOs)
  Aethra.Shared.Infrastructure/ pipelines MediatR, outbox base
  Aethra.Modules.<X>/           bounded contexts (Projects, Deployments, ...)
tests/
  unit, integration, architecture
deploy/
  docker-compose.yml + Dockerfiles
docs/
```

**Regla:** ningún `Modules.X` referencia internals de otro `Modules.Y` — solo eventos en `Shared.Contracts`. Tests de arquitectura (NetArchTest) lo verifican.

## Requisitos

- .NET 10 SDK
- Node.js 24+
- Docker (necesario desde F2 para satélite y deploys)
- PostgreSQL 16 (vía Docker compose en `deploy/`)

## Arrancar en local

```powershell
# 1. Postgres
docker compose -f deploy/docker-compose.yml up -d postgres

# 2. API (escucha en http://localhost:5080)
dotnet run --project apps/api

# 3. Frontend (escucha en http://localhost:3000)
cd apps/web; npm run dev
```

Credenciales por defecto en desarrollo: `admin@aethra.local` / `aethra-dev`.
Override vía variables de entorno:

```powershell
$env:Identity__AdminEmail = "tu@correo.com"
$env:Identity__AdminPasswordSeed = "tu-clave-segura"
dotnet run --project apps/api
```

## Endpoints F0

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/health` | Estado del servicio (público) |
| POST | `/auth/login` | Body `{ email, password }` → setea cookie |
| POST | `/auth/logout` | Cierra sesión |
| GET | `/auth/me` | Datos del usuario autenticado |
| GET | `/context` | Snapshot agregado (proyectos, VMs, servicios) — stub en F0 |
| GET | `/openapi/v1.json` | Documentación OpenAPI 3.1 generada |

## Licencia

Privado — uso personal.
