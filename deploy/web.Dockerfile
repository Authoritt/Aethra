# Imagen del frontend (panel) de Aethra — Next.js 16.
# Se sirve detrás del proxy YARP del central en el mismo origen: las rutas /api, /auth, /hubs
# las atiende el central (endpoints específicos ganan al catch-all del proxy) y el resto va a
# este Next. Por eso NEXT_PUBLIC_API_URL apunta al MISMO host público.
#
# Build: docker build -f deploy/web.Dockerfile \
#          --build-arg NEXT_PUBLIC_API_URL=https://aethra.example.com \
#          -t aethra-web:latest .
# Run:   docker run -d --name aethra-web --network aethra-net aethra-web:latest

FROM node:22-alpine AS build
WORKDIR /app
COPY apps/web/package.json apps/web/package-lock.json ./
# npm install (no ci): el lock se generó en otra arquitectura y faltan las deps nativas
# opcionales de linux-arm64 (@emnapi/*). npm install resuelve las correctas por plataforma.
RUN npm install --no-audit --no-fund
COPY apps/web/ ./
ARG NEXT_PUBLIC_API_URL=https://aethra.example.com
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
# next start necesita .next + node_modules + package.json + public.
COPY --from=build /app/ ./
EXPOSE 3000
CMD ["npm", "start"]
