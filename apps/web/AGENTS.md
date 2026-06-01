<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

## Issues conocidos

(F9.8 — TODO Next 16 i18n cerrado: verificado en `next dev` con `Accept-Language: es-ES,es;q=0.9` y `curl -L` que `/` y `/login` responden 200 sin redirects. El reporte original del smoke F9.6 era cache del navegador o proxy intermedio, no un bug del código. Si vuelve a aparecer, capturar `curl -v` exacto y commit hash antes de tocar config.)
