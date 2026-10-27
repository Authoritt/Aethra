# Topologías de Despliegue y Configuración de CORS en Aethra

Aethra cuenta con dos interfaces HTTP principales:
- **API Central (`apps/api`)**: Desarrollada en ASP.NET Core (.NET 10), expone endpoints REST, OpenAPI, hubs de SignalR, MCP y el proxy inverso YARP. Escucha internamente en el puerto `5080`.
- **Panel Web (`apps/web`)**: Aplicación Next.js (App Router) que se ejecuta en el navegador del usuario y consume directamente la API Central enviando cookies de sesión (`aethra.sid`). Escucha internamente en el puerto `3000`.

Dependiendo de cómo se exponga Aethra a los usuarios, existen dos patrones de arquitectura admitidos: **Same-Origin** (mismo origen detrás de proxy) y **Split-Origin** (orígenes separados).

---

## 1. Topología Same-Origin (Recomendada para Producción)

En esta topología, un reverse proxy o túnel (como Cloudflare Tunnel, Nginx, Caddy o Traefik) unifica la UI y la API bajo un único origen público (por ejemplo, `https://aethra.example.com`).

```
                    Navegador del Usuario
                             │
                             ▼ https://aethra.example.com
                  ┌──────────────────────┐
                  │    Reverse Proxy     │
                  │  (Cloudflare/Nginx)  │
                  └──────────┬───────────┘
                             │
            ┌────────────────┴────────────────┐
            │ /api, /auth, /hubs, /openapi,   │ / (resto de rutas)
            │ /mcp, /webhooks                 │
            ▼                                 ▼
   ┌─────────────────┐               ┌─────────────────┐
   │   aethra-api    │               │   aethra-web    │
   │   puerto 5080   │               │   puerto 3000   │
   └─────────────────┘               └─────────────────┘
```

### Reglas de Enrutamiento del Proxy
- `/api/*`, `/auth/*`, `/hubs/*`, `/openapi/*`, `/mcp`, `/webhooks/*` se redirigen a `http://aethra-api:5080`.
- El resto de rutas (`/*`) se redirigen al contenedor web `http://aethra-web:3000`.

### Configuración en `deploy/.env`
```dotenv
AETHRA_PUBLIC_API_URL=https://aethra.example.com
AETHRA_PUBLIC_WEB_URL=https://aethra.example.com
```

El host del panel NO se lee de `deploy/.env`: el compose no lo pasa al contenedor. Añádelo en
`services.api.environment` de `deploy/docker-compose.yml` (o en un `docker-compose.override.yml`);
sin él, los endpoints del panel responden en cualquier hostname que llegue a la API:
```yaml
services:
  api:
    environment:
      Panel__Hosts__0: aethra.example.com
```

### Ventajas
- **Sin restricciones de CORS**: Dado que tanto los scripts de la UI como las llamadas a la API residen en `https://aethra.example.com`, las peticiones son del mismo origen (*Same-Origin*).
- **Cookies de primer origen**: La cookie de autenticación `aethra.sid` se emite y transmite de forma segura con `SameSite=Lax`.
- **Terminación TLS unificada**: Un solo certificado cubre toda la superficie del sistema.

---

## 2. Topología Split-Origin (LAN o Dominios Separados)

En este escenario, el panel web y la API se exponen en orígenes distintos (diferente puerto, IP o dominio).

Casos de uso típicos:
- **Instalación en red local (LAN)**: La UI se accede en `http://192.168.1.50:3000` y la API en `http://192.168.1.50:5080`.
- **Despliegue multi-dominio**: La UI corre en `https://panel.example.com` y la API en `https://api.example.com`.
- **Desarrollo o pruebas autohospedadas**: Acceso directo a los puertos mapeados del Docker Compose (`:3000` y `:5080`).

```
                    Navegador del Usuario
                      │                │
     Origin:          │                │ Peticiones fetch()
     http://lan:3000  │                │ con cookies/credenciales
                      ▼                ▼
             ┌────────────────┐   ┌────────────────┐
             │   aethra-web   │   │   aethra-api   │
             │  (puerto 3000) │   │  (puerto 5080) │
             └────────────────┘   └────────────────┘
                                           ▲
                                           │
                         Valida CORS: origen en allowlist
                         Access-Control-Allow-Origin: http://lan:3000
                         Access-Control-Allow-Credentials: true
```

### Requisitos de CORS y Seguridad

1. **Permiso Explícito de Origen**: Para peticiones con credenciales (cookies de sesión), la API debe devolver:
   ```http
   Access-Control-Allow-Origin: <origen exacto de la UI>
   Access-Control-Allow-Credentials: true
   ```
2. **Prohibición de Comodines (`*`)**: Los navegadores modernos prohíben terminantemente usar `Access-Control-Allow-Origin: *` cuando se transmiten credenciales (`credentials: 'include'`). Por diseño de seguridad (#87), Aethra valida los orígenes al arrancar y **aborta el inicio** si detecta un comodín (`*`) en la lista de orígenes permitidos.
3. **Formato Estricto de Origen**: Cada entrada configurada debe ser una URI absoluta `http` o `https` sin ruta ni barra final (ejemplo: `http://192.168.1.50:3000` o `https://panel.example.com`). Rutas como `http://lan:3000/` o `http://lan:3000/ui` son rechazadas al arrancar para evitar desajustes en el cotejo de encabezados.
4. **Denegación por Defecto**: Cualquier origen no configurado explícitamente es rechazado sin encabezados CORS.

### Configuración en `deploy/.env`
```dotenv
# Origen donde el navegador contacta la API (se hornea en Next.js al compilar)
AETHRA_PUBLIC_API_URL=http://192.168.1.50:5080

# Origen donde el navegador carga la interfaz web (se inyecta en la API vía Cors:AllowedOrigins)
AETHRA_PUBLIC_WEB_URL=http://192.168.1.50:3000
```

> **Nota:** Tras cambiar `AETHRA_PUBLIC_API_URL`, es necesario reconstruir la imagen web para aplicar el cambio en el bundle de Next.js:
> ```bash
> docker compose up -d --build web api
> ```

### Configuración de Múltiples Orígenes
Si necesitas permitir varios orígenes de confianza (por ejemplo, acceso por LAN y acceso por VPN simultáneos), configúralos en `apps/api/appsettings.json` o mediante variables de entorno del sistema:

**En `appsettings.json`:**
```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://192.168.1.50:3000",
      "https://panel.vpn.internal"
    ]
  }
}
```

**Vía Variables de Entorno (Docker / Linux):**
```dotenv
Cors__AllowedOrigins__0=http://192.168.1.50:3000
Cors__AllowedOrigins__1=https://panel.vpn.internal
```
O también mediante lista separada por comas:
```dotenv
Cors__AllowedOrigins=http://192.168.1.50:3000,https://panel.vpn.internal
```

---

## 3. Entorno de Desarrollo Local

Para desarrollo local en la máquina anfitriona, los siguientes orígenes están habilitados de manera predeterminada sin requerir configuración adicional:
- `http://localhost:3000` (puerto estándar de Next.js)
- `http://localhost:5173` (puerto estándar de Vite)
- `http://127.0.0.1:3000`
- `http://127.0.0.1:5173`
