# Imagen del central Aethra (API + módulos + YARP + SignalR + MCP).
# El frontend Next.js (apps/web) se sirve aparte; esta imagen es solo el control plane.
#
# Build:   docker build -f deploy/api.Dockerfile -t aethra-central:latest .
# Run:     docker run -d --name aethra-central --network aethra-net -p 5080:5080 \
#            -e ConnectionStrings__Aethra="Host=aethra-postgres;Port=5432;Database=aethra;Username=aethra;Password=..." \
#            -e Identity__AdminEmail=... -e Identity__AdminPasswordSeed=... \
#            -e ASPNETCORE_ENVIRONMENT=Development \
#            -v aethra-keys:/keys aethra-central:latest

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/api/Aethra.Api.csproj
RUN dotnet publish apps/api/Aethra.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# git: el BuildContextBuilder clona los repos de las apps con el CLI de git (transporte HTTPS
# fiable en Linux/ARM). ca-certificates para validar los certs de GitHub/GitLab.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# DataProtection persistente (montar volumen en /keys). Sin esto los secretos cifrados se
# vuelven ilegibles tras reiniciar el contenedor.
ENV DataProtection__KeyDir=/keys
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Aethra.Api.dll"]
