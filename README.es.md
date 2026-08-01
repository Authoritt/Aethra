# Aethra

**Una sola plataforma para desplegar, enrutar, certificar, monitorear y operar tu infraestructura.**

[![CI](https://github.com/Authoritt/Aethra/actions/workflows/ci.yml/badge.svg)](https://github.com/Authoritt/Aethra/actions/workflows/ci.yml)
[![Licencia](https://img.shields.io/badge/licencia-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

> 🇬🇧 [Read this in English](README.md)

Aethra unifica en un único sistema —con una base de datos compartida y una sola UI— lo que hoy te obliga
a saltar entre cuatro herramientas distintas: **despliegue Git→Docker** (en lugar de Coolify),
**reverse proxy + TLS automático** (en lugar de Traefik), **monitoreo de uptime** (en lugar de Uptime Kuma)
y **métricas de VMs y contenedores** (en lugar de Beszel). El proyecto, la URL pública, las variables de
entorno, el monitor que la vigila y la nota con sus credenciales viven en el mismo lugar — no en cuatro
lugares que nadie sincroniza.

**Multi-tenant nativo:** una `Template` (un repo Git) puede correr para N clientes (`Client`) en M
ambientes (`Instance`), cada uno con sus propias variables, secretos, dominio y deploy independiente. Una
sola imagen se construye y se despliega a todos los clientes que la usan — sin duplicar configuración.

**Pensado para que lo opere un agente, no solo una persona.** El servidor MCP embebido expone las
operaciones críticas como herramientas tipadas, y cada respuesta trae
`next_actions: [{ tool, why, suggested_args }]` — para que el agente sepa qué sigue en vez de tener que
deducir tu modelo de datos.

---

## Tu IA puede operar casi todo esto

No es un punto del roadmap. Apunta a Claude —o a cualquier agente con MCP— a `wss://aethra/mcp`, dale una
API key con scopes, y operas tu infraestructura preguntando:

> **"Despliega el último main al ambiente de staging de la plantilla de facturación."**
> → `aethra_list_context` para ubicarlo, `aethra_trigger_build`, luego `aethra_trigger_deployment`, y te
> reporta el resultado REAL del healthcheck en vez de dar por hecho que funcionó.

> **"¿Cuál de mis proyectos está caído ahora mismo?"**
> → `aethra_get_monitor_status` sobre todos los monitores, agrupado por proyecto.

> **"¿Esa VM nueva sí está reportando? ¿Cómo va de disco?"**
> → `aethra_query_metrics` — CPU, RAM, disco y stats por contenedor, saliendo del satélite.

> **"Ponle el dominio shop.acme.com a esa instancia."**
> → `aethra_attach_domain` crea el CNAME en Cloudflare, provisiona el certificado y cambia la ruta de YARP.

> **"Esta app necesita base de datos."**
> → `aethra_bind_service` provisiona un Postgres real con su usuario y contraseña, y te inyecta la
> cadena de conexión como env var y como secreto.

Dos decisiones de diseño hacen que esto sea seguro de dejar encendido:

- **La llave del agente no puede escalar.** Las API keys llevan scopes granulares (`deployments:write`,
  `projects:read`), y los endpoints que crean API keys o leen secretos están fuera del alcance por diseño.

- **`dry_run` existe, pero no en todas las herramientas mutantes todavía.** Las herramientas que lo
  soportan aceptan `dryRun: true` y devuelven el plan y el endpoint que se habría llamado, sin ejecutar
  nada. Las herramientas que aún **no** lo soportan lo indican explícitamente en su descripción —
  `[No soporta dry_run: esta operación se ejecuta de inmediato]` — para que el agente sepa antes de
  llamarlas. En este momento, 29 de 76 herramientas mutantes soportan `dry_run`; el resto lo indica en
  su descripción. Consulta el [seguimiento del issue #47](https://github.com/Authoritt/Aethra/issues/47)
  para ver el estado actual.

---

## Inicio rápido

```bash
git clone https://github.com/Authoritt/Aethra
cd Aethra
cp .env.example .env          # edita con tus credenciales
docker compose up -d
```

Abre `http://localhost:5000`. Crea un proyecto, apunta a un repo, lanza el primer build.

---

## Por qué otro dashboard de infraestructura

La mayoría de las plataformas de autoalojamiento resuelven bien una cosa. Aethra resuelve cuatro cosas
juntas porque en producción real esas cuatro cosas no son independientes:

| Lo que ya tienes | Lo que Aethra reemplaza |
|---|---|
| Coolify / Dokku | Despliegue Git→Docker multi-tenant |
| Traefik / Caddy | Reverse proxy + TLS automático |
| Uptime Kuma | Monitoreo de uptime con alertas |
| Beszel / Netdata | Métricas de VM y contenedores |

La diferencia no es la lista de features — es que los cuatro comparten la misma base de datos. Cuando
un deploy termina, el monitor ya sabe qué URL vigilar. Cuando un cliente se elimina, sus dominios,
certificados y métricas desaparecen con él.

---

## Arquitectura

```
┌─────────────────────────────────────────────────┐
│                   Aethra Core                   │
│                                                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐  │
│  │ Deploy   │  │  Proxy   │  │  Monitoring  │  │
│  │ Engine   │  │  (YARP)  │  │   Engine     │  │
│  └──────────┘  └──────────┘  └──────────────┘  │
│                                                 │
│  ┌─────────────────────────────────────────┐    │
│  │           Shared Database               │    │
│  │     (Projects, Clients, Instances)      │    │
│  └─────────────────────────────────────────┘    │
│                                                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐  │
│  │   MCP    │  │   REST   │  │  Satellite   │  │
│  │  Server  │  │   API    │  │   Agent      │  │
│  └──────────┘  └──────────┘  └──────────────┘  │
└─────────────────────────────────────────────────┘
```

**Satellite Agent** — un proceso liviano que corre en cada VM, reporta métricas y ejecuta comandos de
despliegue locales. Se registra solo contra el core; no necesita IP pública.

---

## Herramientas MCP disponibles

> **Nota sobre `dry_run`:** No todas las herramientas mutantes soportan `dry_run` todavía. Las que no lo
> soportan lo indican con `[No soporta dry_run: esta operación se ejecuta de inmediato]` en su
> descripción. No asumas que una herramienta mutante soporta `dry_run` a menos que su descripción lo
> confirme explícitamente.

### Solo lectura
| Herramienta | Descripción |
|---|---|
| `aethra_list_context` | Lista proyectos, clientes, instancias y ambientes |
| `aethra_get_monitor_status` | Estado actual de todos los monitores |
| `aethra_query_metrics` | CPU, RAM, disco y métricas de contenedores |
| `aethra_list_deployments` | Historial de despliegues |
| `aethra_get_logs` | Logs de un contenedor o build |

### Mutantes con `dry_run`
| Herramienta | Descripción |
|---|---|
| `aethra_trigger_build` | Dispara un build Git→Docker |
| `aethra_trigger_deployment` | Despliega una imagen a una instancia |
| `aethra_bind_service` | Provisiona y conecta un servicio (ej. Postgres) |
| `aethra_create_project` | Crea un proyecto nuevo |
| `aethra_delete_instance` | Elimina una instancia |

### Mutantes **sin** `dry_run` — se ejecutan de inmediato
| Herramienta | Descripción |
|---|---|
| `aethra_trigger_deploy` | **[No soporta dry_run]** Dispara un deploy completo |
| `aethra_deploy_instance_native` | **[No soporta dry_run]** Despliega instancia nativa |
| `aethra_delete_monitor` | **[No soporta dry_run]** Elimina un monitor |
| `aethra_set_env_