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
  `projects:read`), y los endpoints que crean API keys o leen secretos están excluidos del plano MCP — el
  agente no puede crear sus propias llaves ni exfiltrar credenciales.

- **El agente no puede aprobar sus propios cambios.** Los cambios destructivos (borrar una instancia,
  rotar un secreto compartido) requieren confirmación humana vía UI antes de ejecutarse.

---

## Inicio rápido

> **Tiempo estimado:** 10–15 minutos en una máquina con Docker instalado y puertos 80/443 libres.
> Si esos puertos están ocupados, lee la sección [Puertos ocupados](#puertos-ocupados) antes de continuar.

### Requisitos previos

| Requisito | Versión mínima | Notas |
|-----------|---------------|-------|
| Docker | 24.0 | `docker --version` para verificar |
| Docker Compose | v2.20 (plugin) | `docker compose version` — nota: sin guión |
| Git | cualquiera | para clonar |
| Puertos libres | 80, 443 | ver [Puertos ocupados](#puertos-ocupados) si no |
| Hostname público | recomendado | necesario para TLS real via ACME; sin él puedes usar modo local |

> **arm64 (Apple Silicon, Raspberry Pi, etc.):** Aethra corre en arm64. Si encuentras un problema
> específico de arquitectura, inclúyelo en tu reporte — hay historia conocida con Chromium en este stack
> y queremos saber qué sobrevive.

### 1. Clonar y configurar

```bash
git clone https://github.com/Authoritt/Aethra.git
cd Aethra
cp .env.example .env
```

Abre `.env` en tu editor. Los campos obligatorios para el primer arranque son:

```dotenv
# Dominio donde correrá Aethra (puede ser localhost para pruebas locales)
AETHRA_DOMAIN=aethra.example.com

# Secreto para JWT — genera uno con: openssl rand -hex 32
AETHRA_JWT_SECRET=cambia_esto_por_algo_aleatorio

# Email para notificaciones ACME/Let's Encrypt
ACME_EMAIL=tu@email.com

# Contraseña inicial del administrador (cámbiala después del primer login)
AETHRA_ADMIN_PASSWORD=cambia_esto_tambien
```

> **Modo local (sin dominio público):** Si usas `localhost` o una IP, el certificado TLS será
> autofirmado. Tu navegador mostrará una advertencia — esto es esperado. ACME/Let's Encrypt requiere
> un hostname públicamente resolvible.

> **Detrás de NAT:** Si tu servidor está detrás de NAT, ACME no podrá completar el challenge HTTP-01.
> El arranque no fallará, pero el certificado no se expedirá y verás errores TLS. Usa `ACME_EMAIL` de
> todas formas — lo necesitarás cuando el hostname sea público.

### 2. Migraciones de base de datos

Por defecto, las migraciones **no** se aplican automáticamente fuera del entorno `Development`.
Para el primer arranque, tienes dos opciones:

**Opción A — Aplicar migraciones automáticamente (recomendado para primer arranque):**

En tu `.env`, agrega:

```dotenv
Aethra__ApplyMigrationsOnStart=true
```

**Opción B — Aplicar migraciones manualmente:**

```bash
docker compose run --rm aethra dotnet aethra migrate
```

> **Si omites este paso** y `ApplyMigrationsOnStart` no está en `true`, la aplicación arrancará pero
> fallará al intentar leer o escribir en la base de datos. El error típico es:
> `relation "AspNetUsers" does not exist` o similar. No es un bug — es que la base de datos está vacía.

### 3. Levantar los servicios

```bash
docker compose up -d
```

Para ver los logs en tiempo real:

```bash
docker compose logs -f aethra
```

Espera hasta ver una línea similar a:

```
aethra  | Application started. Press Ctrl+C to shut down.
```

Si ves errores antes de esa línea, la sección [Solución de problemas](#solución-de-problemas) los cubre.

### 4. Crear la primera cuenta

Abre `https://<AETHRA_DOMAIN>` (o `http://localhost` si usas modo local sin TLS).

En un **primer arranque con base de datos vacía**, Aethra detecta que no hay ningún usuario y muestra
el formulario de registro de administrador directamente — no necesitas un código de invitación.

> **Si el formulario no aparece** y en cambio ves el login normal, la base de datos no está vacía
> (quizás de un arranque anterior). En ese caso:
> - Si configuraste `AETHRA_ADMIN_PASSWORD` en `.env`, usa `admin@aethra.local` como email y esa
>   contraseña como primer login.
> - Si no, ejecuta `docker compose run --rm aethra dotnet aethra create-admin` para crear un usuario
>   desde la línea de comandos.

Completa el formulario con tu email y una contraseña segura. Después del primer login, **cambia la
contraseña** desde el menú de perfil.

### 5. Verificar que todo funciona

```bash
# Estado de los contenedores
docker compose ps

# Health check de la API
curl -k https://<AETHRA_DOMAIN>/health
# Respuesta esperada: {"status":"Healthy"}
```

Si `curl` devuelve `{"status":"Healthy"}`, la instalación está completa.

---

## Puertos ocupados

Si los puertos 80 o 443 ya están en uso (nginx, Apache, otro proxy, etc.),