#!/usr/bin/env bash
# Extrae la configuración de un proyecto desde Coolify y la convierte en payloads JSON
# listos para los endpoints REST de Aethra. Versión bash del .ps1 equivalente.
#
# Requisitos: bash 4+, curl, jq.
#
# Uso:
#   COOLIFY_URL=https://coolify.miempresa.com \
#   COOLIFY_TOKEN=cf_xxx \
#   PROJECT_ID=42 \
#   OUTPUT_DIR=./migration-output \
#   ./migrate-from-coolify.sh

set -euo pipefail

: "${COOLIFY_URL:?Falta COOLIFY_URL}"
: "${COOLIFY_TOKEN:?Falta COOLIFY_TOKEN}"
: "${PROJECT_ID:?Falta PROJECT_ID}"
OUTPUT_DIR="${OUTPUT_DIR:-./migration-output}"

api() {
    curl -fsSL -H "Authorization: Bearer ${COOLIFY_TOKEN}" "${COOLIFY_URL}/api/v1$1"
}

slugify() {
    echo "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g'
}

project_dir="${OUTPUT_DIR}/${PROJECT_ID}"
mkdir -p "${project_dir}"/{applications,env-vars,domains,service-bindings}

echo "==> Leyendo proyecto ${PROJECT_ID} desde ${COOLIFY_URL}"
project_json=$(api "/projects/${PROJECT_ID}")

name=$(echo "$project_json" | jq -r '.name')
slug=$(slugify "$name")
color=$(echo "$project_json" | jq -r '.color // "#7c3aed"')

jq -n \
    --arg name "$name" \
    --arg slug "$slug" \
    --arg color "$color" \
    '{ name: $name, slug: $slug, color: $color, icon: "package" }' \
    > "${project_dir}/project.json"
echo "    project.json escrito ($name)"

echo "==> Leyendo applications"
resources_json=$(api "/projects/${PROJECT_ID}/resources")
app_count=0

while IFS= read -r resource; do
    type=$(echo "$resource" | jq -r '.type')
    if [[ "$type" != "application" ]]; then continue; fi
    app_count=$((app_count + 1))

    res_name=$(echo "$resource" | jq -r '.name')
    res_slug=$(slugify "$res_name")
    res_id=$(echo "$resource" | jq -r '.id')

    echo "    [app] $res_name → $res_slug"

    base_directory=$(echo "$resource" | jq -r '.base_directory // ""')
    watch_paths='[]'
    if [[ -n "$base_directory" && "$base_directory" != "null" ]]; then
        clean=$(echo "$base_directory" | sed 's|^/||')
        watch_paths=$(jq -n --arg p "${clean}/**" '[$p]')
    fi

    build_pack=$(echo "$resource" | jq -r '.build_pack // "dockerfile"')
    build_type=$([ "$build_pack" = "dockercompose" ] && echo "DockerCompose" || echo "Dockerfile")

    echo "$resource" | jq \
        --arg slug "$res_slug" \
        --argjson watch "$watch_paths" \
        --arg build_type "$build_type" \
        '{
            name: .name,
            slug: $slug,
            source: {
                git_repo_url: .git_repository,
                branch: (.git_branch // "main"),
                webhook_secret: .webhook_secret,
                base_directory: .base_directory,
                watch_paths: $watch
            },
            build: {
                type: $build_type,
                dockerfile_path: (.dockerfile_location // "Dockerfile"),
                compose_file_path: .docker_compose_location
            },
            runtime: {
                container_name: $slug,
                ports: [ { container_port: (.ports_exposes // 3000) } ]
            }
        }' > "${project_dir}/applications/${res_slug}.json"

    echo "    [env] cargando env vars de $res_slug"
    api "/applications/${res_id}/envs" | jq \
        '{ vars: [.[] | {
            key: .key,
            value: .value,
            is_build_time: (.is_build_time == true),
            is_runtime: (.is_build_time != true),
            is_secret: (.is_secret == true),
            is_literal: (.is_literal == true),
            is_multiline: (.is_multiline == true)
        }] }' > "${project_dir}/env-vars/${res_slug}.json"

    echo "    [dom] dominios de $res_slug"
    echo "$resource" | jq \
        '{ domains: [
            (.fqdn // "") | split(",") | .[] | gsub("\\s+"; "") | select(length > 0)
            | gsub("^https?://"; "") | sub("/.*$"; "")
            | { hostname: ., record_type: "CNAME", proxied: true }
        ] }' > "${project_dir}/domains/${res_slug}.json"

    bindings=$(echo "$resource" | jq -c '
        [
            (.environment_relations // []) | .[] | select(.resource.type // "" |
                ascii_downcase | IN("postgresql","postgres","redis","rabbitmq","mysql","mongodb")) |
            {
                coolify_resource_id: .resource.id,
                coolify_resource_name: .resource.name,
                coolify_resource_type: (.resource.type | ascii_downcase),
                suggested_aethra_template: (
                    if (.resource.type | ascii_downcase) | IN("postgresql","postgres") then "postgres-16"
                    elif (.resource.type | ascii_downcase) == "redis" then "redis-7"
                    elif (.resource.type | ascii_downcase) == "rabbitmq" then "rabbitmq-3-mgmt"
                    else "manual" end
                ),
                permissions: "owner"
            }
        ]
    ')
    if [[ "$bindings" != "[]" ]]; then
        jq -n --argjson b "$bindings" --arg slug "$res_slug" \
            '{ bindings: ($b | map(. + { suggested_resource_name: ($slug + "_db"), suggested_env_var_prefix: "" })) }' \
            > "${project_dir}/service-bindings/${res_slug}.json"
    fi
done < <(echo "$resources_json" | jq -c '.[]')

echo ""
echo "==> Extracción completa"
echo "    Proyecto:     $name ($app_count applications)"
echo "    Output:       $project_dir"
echo ""
echo "Próximos pasos:"
echo "  1. Revisa los JSON en $project_dir (especialmente env-vars/ por si hay secretos)"
echo "  2. Ajusta el campo runtime.target_vm_id en cada app .json (el script no lo sabe)"
echo "  3. POSTea cada uno contra Aethra siguiendo docs/migration-from-coolify.md §1.2-§1.4"
