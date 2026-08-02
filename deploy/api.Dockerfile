# Imagen del central Aethra (API + módulos + YARP + SignalR + MCP).
# El frontend Next.js (apps/web) se sirve aparte; esta imagen es solo el control plane.
#
# Build:   docker build -f deploy/api.Dockerfile -t aethra-central:latest .
# Run:     docker run -d --name aethra-central --network aethra-net -p 5080:5080 \
#            -e ConnectionStrings__Aethra="Host=aethra-postgres;Port=5432;Database=aethra;Username=aethra;Password=..." \
#            -e Identity__AdminEmail=... -e Identity__AdminPasswordSeed=... \
#            -e ASPNETCORE_ENVIRONMENT=Development \
#            -v aethra-keys:/keys aethra-central:latest

# --platform=$BUILDPLATFORM + -a $TARGETARCH: compilamos CRUZADO desde la arquitectura del
# runner en vez de emular la de destino con QEMU. Un build arm64 emulado de .NET tarda ~8x
# mas; asi solo la capa de runtime se resuelve para la arquitectura de destino.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet restore apps/api/Aethra.Api.csproj -a $TARGETARCH
RUN dotnet publish apps/api/Aethra.Api.csproj -c Release -o /app/publish --no-restore -a $TARGETARCH --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# git: el BuildContextBuilder clona los repos de las apps con el CLI de git (transporte HTTPS
# fiable en Linux/ARM). ca-certificates para validar los certs de GitHub/GitLab.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# DataProtection persistente (montar volumen en /keys). Sin esto los secretos cifrados se
# vuelven ilegibles tras reiniciar el contenedor.
ENV DataProtection__KeyDir=/keys
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
# El MCP Registry verifica la PROPIEDAD de una imagen OCI comprobando que esta anotacion
# coincida EXACTAMENTE con el campo "name" de server.json. Si cambias una, cambia la otra.
LABEL io.modelcontextprotocol.server.name="io.github.Authoritt/aethra"

ENTRYPOINT ["dotnet", "Aethra.Api.dll"]
