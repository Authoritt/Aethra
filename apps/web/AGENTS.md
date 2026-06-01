<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

## Issues conocidos

### TODO F9.7: Next 16 i18n auto-redirect en /login (investigar)

Un smoke test reportó `GET /login -> 307 -> /es/login -> 404`. Investigación realizada en F9.6:

- No existe `proxy.ts` ni `middleware.ts` en `apps/web/`.
- `next.config.ts` está vacío (no hay clave `i18n`).
- El App Router de Next 16 NO trae auto-redirección por `Accept-Language` (la doc en `node_modules/next/dist/docs/01-app/02-guides/internationalization.md` requiere implementar manualmente un `proxy.ts` con `NextResponse.redirect`).
- La opción legacy `i18n: { locales, defaultLocale, localeDetection }` solo aplica al Pages Router (`node_modules/next/dist/docs/02-pages/02-guides/internationalization.md`), no al App Router.
- El layout raíz declara `<html lang="es">` pero eso no causa redirecciones de URL.
- Búsqueda con grep de `/es/` y `307` en el repo no encontró ningún origen.

Hipótesis abiertas para F9.7:
1. Cache del navegador / proxy intermedio durante el smoke test (no reproducible en código).
2. Algún paquete instalado en `node_modules` (recharts, react-markdown) inyecta un handler — investigar.
3. La cookie de sesión expirada redirige a una URL preestablecida en otro lugar.

Acción inmediata: NO se aplicó fix porque no se identificó la causa. El comportamiento debe verificarse con `next dev` corriendo y reproducirse antes de tocar config.
