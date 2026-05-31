<#
.SYNOPSIS
  Extrae la configuración de un proyecto desde Coolify y la convierte en payloads JSON
  listos para los endpoints REST de Aethra.

.DESCRIPTION
  No automatiza la migración completa (eso sigue siendo decisión por proyecto), pero ahorra
  el trabajo de transcribir manualmente cada campo. Lee la API de Coolify directamente y
  emite un directorio con:
    - project.json
    - applications/{slug}.json
    - env-vars/{slug}.json
    - domains/{slug}.json
    - service-bindings/{slug}.json (si detecta Postgres/Redis attached)

  Después, otro script (o tú con curl) los POSTea contra Aethra. Esta separación intencional
  permite revisar lo que se va a crear antes de mutar nada.

.PARAMETER CoolifyUrl
  Base URL del Coolify, ej. https://coolify.miempresa.com

.PARAMETER CoolifyToken
  API token con permisos read en el Coolify.

.PARAMETER ProjectId
  ID del proyecto en Coolify (numérico o UUID según versión).

.PARAMETER OutputDir
  Directorio donde se escriben los JSON. Default: ./migration-output/{ProjectId}.

.EXAMPLE
  ./migrate-from-coolify.ps1 -CoolifyUrl https://coolify.miempresa.com `
                              -CoolifyToken cf_xxx -ProjectId 42

.NOTES
  Requisitos: PowerShell 7+ (uses ?? operator).
  Se ejecuta SOLO localmente. No POSTea nada a Aethra — output es estático para revisión.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CoolifyUrl,
    [Parameter(Mandatory)][string]$CoolifyToken,
    [Parameter(Mandatory)][string]$ProjectId,
    [string]$OutputDir = "./migration-output"
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $CoolifyToken" }
$projectDir = Join-Path $OutputDir $ProjectId
New-Item -ItemType Directory -Path $projectDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir "applications") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir "env-vars") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir "domains") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir "service-bindings") -Force | Out-Null

function Get-CoolifyApi {
    param([string]$Path)
    $url = "$CoolifyUrl/api/v1$Path"
    Invoke-RestMethod -Uri $url -Headers $headers -Method Get
}

function ConvertTo-AethraSlug {
    param([string]$Name)
    return ($Name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
}

Write-Host "==> Leyendo proyecto $ProjectId desde $CoolifyUrl"
$coolifyProject = Get-CoolifyApi "/projects/$ProjectId"

$aethraProject = @{
    name = $coolifyProject.name
    slug = ConvertTo-AethraSlug $coolifyProject.name
    color = ($coolifyProject.color ?? "#7c3aed")
    icon = ($coolifyProject.icon ?? "package")
} | ConvertTo-Json -Depth 10

$aethraProject | Out-File (Join-Path $projectDir "project.json") -Encoding utf8
Write-Host "    project.json escrito ($($coolifyProject.name))"

Write-Host "==> Leyendo applications"
$resources = Get-CoolifyApi "/projects/$ProjectId/resources"
$appCount = 0

foreach ($resource in $resources) {
    if ($resource.type -ne "application") { continue }
    $appCount++
    $slug = ConvertTo-AethraSlug $resource.name

    Write-Host "    [app] $($resource.name) → $slug"

    $watchPaths = @()
    if ($resource.base_directory) {
        $relPath = $resource.base_directory.TrimStart('/')
        $watchPaths = @("$relPath/**")
    }

    $appJson = @{
        name = $resource.name
        slug = $slug
        source = @{
            git_repo_url = $resource.git_repository
            branch = ($resource.git_branch ?? "main")
            webhook_secret = $resource.webhook_secret
            base_directory = $resource.base_directory
            watch_paths = $watchPaths
        }
        build = @{
            type = if ($resource.build_pack -eq "dockercompose") { "DockerCompose" } else { "Dockerfile" }
            dockerfile_path = ($resource.dockerfile_location ?? "Dockerfile")
            compose_file_path = $resource.docker_compose_location
        }
        runtime = @{
            container_name = $slug
            ports = @(@{ container_port = ($resource.ports_exposes ?? 3000) })
        }
    } | ConvertTo-Json -Depth 10

    $appJson | Out-File (Join-Path $projectDir "applications/$slug.json") -Encoding utf8

    Write-Host "    [env] cargando env vars de $slug"
    $envVars = Get-CoolifyApi "/applications/$($resource.id)/envs"
    $vars = $envVars | ForEach-Object {
        @{
            key = $_.key
            value = $_.value
            is_build_time = ($_.is_build_time -eq $true)
            is_runtime = ($_.is_build_time -ne $true)
            is_secret = ($_.is_secret -eq $true)
            is_literal = ($_.is_literal -eq $true)
            is_multiline = ($_.is_multiline -eq $true)
        }
    }
    @{ vars = $vars } | ConvertTo-Json -Depth 10 |
        Out-File (Join-Path $projectDir "env-vars/$slug.json") -Encoding utf8

    Write-Host "    [dom] dominios de $slug"
    $domains = @()
    if ($resource.fqdn) {
        $hostnames = $resource.fqdn -split ',' | ForEach-Object { $_.Trim() } |
            Where-Object { $_.Length -gt 0 }
        foreach ($hostname in $hostnames) {
            $clean = ($hostname -replace '^https?://', '') -replace '/.*$', ''
            $domains += @{
                hostname = $clean
                record_type = "CNAME"
                proxied = $true
            }
        }
    }
    @{ domains = $domains } | ConvertTo-Json -Depth 10 |
        Out-File (Join-Path $projectDir "domains/$slug.json") -Encoding utf8

    if ($resource.environment_relations) {
        $bindings = @()
        foreach ($rel in $resource.environment_relations) {
            $type = ($rel.resource.type ?? "").ToLowerInvariant()
            if ($type -in @("postgresql", "postgres", "redis", "rabbitmq", "mysql", "mongodb")) {
                $bindings += @{
                    coolify_resource_id = $rel.resource.id
                    coolify_resource_name = $rel.resource.name
                    coolify_resource_type = $type
                    suggested_aethra_template = switch ($type) {
                        "postgresql" { "postgres-16" }
                        "postgres" { "postgres-16" }
                        "redis" { "redis-7" }
                        "rabbitmq" { "rabbitmq-3-mgmt" }
                        default { "manual" }
                    }
                    suggested_resource_name = (ConvertTo-AethraSlug $resource.name) + "_db"
                    suggested_env_var_prefix = ""
                    permissions = "owner"
                }
            }
        }
        if ($bindings.Count -gt 0) {
            @{ bindings = $bindings } | ConvertTo-Json -Depth 10 |
                Out-File (Join-Path $projectDir "service-bindings/$slug.json") -Encoding utf8
        }
    }
}

Write-Host ""
Write-Host "==> Extracción completa"
Write-Host "    Proyecto:     $($coolifyProject.name) ($appCount applications)"
Write-Host "    Output:       $projectDir"
Write-Host ""
Write-Host "Próximos pasos:"
Write-Host "  1. Revisa los JSON en $projectDir (especialmente env-vars/ por si hay secretos)"
Write-Host "  2. Ajusta el campo runtime.target_vm_id en cada app .json (el script no lo sabe)"
Write-Host "  3. POSTea cada uno contra Aethra siguiendo docs/migration-from-coolify.md §1.2-§1.4"
Write-Host ""
Write-Host "Tip: usa la API key con scope * temporal para el bulk import, luego revoca."
