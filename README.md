# Aethra

**One platform to deploy, route, certify, monitor and operate your infrastructure.**

[![CI](https://github.com/Authoritt/Aethra/actions/workflows/ci.yml/badge.svg)](https://github.com/Authoritt/Aethra/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![PRs welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

> 🇪🇸 [Léeme en español](README.es.md)

![Aethra console: signing in to a fresh install, creating the first environment, and watching the pending-configuration checklist go from three items to two](docs/assets/onboarding.gif)

<sub>A fresh install, start to finish: sign in → three pending setup items → create the first environment → the checklist drops to two. Recorded against a throwaway local instance with an empty database — the software actually running, not a mockup.</sub>

Aethra unifies — in a single system, with one shared database and one UI — what today forces you to
jump between four separate tools: **Git→Docker deploys** (instead of Coolify), **reverse proxy + automatic
TLS** (instead of Traefik), **uptime monitoring** (instead of Uptime Kuma) and **VM/container metrics**
(instead of Beszel). The project, its public URL, its environment variables, the monitor watching it and
the note holding its credentials all live in the same place — not in four places nobody keeps in sync.

**Natively multi-tenant.** One `Template` (a Git repo) can run for N clients (`Client`) across M
environments (`Instance`), each with its own variables, secrets, domain and independent deploy. One image
is built once and deployed to every client that uses it, without duplicating configuration.

**Built to be operated by agents, not just by people.** The embedded MCP (Model Context Protocol) server
exposes the critical operations as typed tools, and every tool response carries
`next_actions: [{ tool, why, suggested_args }]` — so an agent knows what to do next instead of
reverse-engineering your data model. That is the part most deploy platforms leave to luck.

---

## Your AI can run most of this

This is not a roadmap item. Point Claude — or any MCP-capable agent — at `https://aethra/mcp`, give it a
scoped API key, and you operate your infrastructure by asking:

> **"Deploy the latest main to the staging instance of the billing template."**
> → `aethra_list_context` to find it, then `aethra_deploy_instance_native`, and it
> reports the real healthcheck result instead of assuming it worked.

> **"Which of my projects is unhealthy right now?"**
> → `aethra_get_monitor_status` across every monitor, grouped by project.

> **"Is that new VM actually reporting? What is its disk at?"**
> → `aethra_query_metrics` — CPU, RAM, disk and per-container stats coming off the satellite.

> **"Attach shop.acme.com to that instance."**
> → `aethra_attach_domain` creates the Cloudflare CNAME, provisions the certificate and swaps the
> YARP route.

> **"This app needs a database."**
> → `aethra_bind_service` provisions a real Postgres with its own user and password and injects the
> connection string as an env var and a secret.

Two design decisions make this safe enough to actually leave on:

- **The agent's key cannot escalate.** API keys carry granular scopes (`deployments:write`,
  `projects:read`). The endpoints that mint API keys or read secrets are cookie-only, so an agent can
  deploy to production and still cannot grant itself anything.
- **Results are real, not optimistic.** Deploy tools return the healthcheck outcome; metrics come from
  the satellite. An agent reporting "deployed successfully" is repeating what the platform observed, not
  narrating what it hoped.

---

## What it does under the hood

| Capability | How it works |
|---|---|
| **Git→Docker deploys** | An HMAC-signed webhook triggers a `Build` (shallow clone → docker/podman build → push to the internal registry). On completion it fans out to N `Deployment`s (pull → run → healthcheck → atomic swap of the YARP route). 1 build, N deployments. |
| **Reverse proxy + TLS** | YARP embedded in the central process. Routes live in the database; on change, `IProxyConfigService.Reload()` updates YARP hot. Let's Encrypt via Certes with automatic renewal (hourly worker, configurable window). |
| **Multi-tenant** | Auto-hostname `{template}-{client}-{env}.{base-domain}` when an Instance is created. Optional custom domain with a Cloudflare CNAME. Variables and secrets resolve in cascade: `Instance > Client > Template > Project`. |
| **Monitoring** | `MonitorWorker` runs HTTP probes on a configurable tick, each probe in its own scope so one cannot block the others. State changes emit integration events that reach SignalR (live UI) and the project timeline. |
| **VM + Docker metrics** | A lightweight .NET satellite connects to the central over SignalR (persistent WebSocket — outbound 443 only, no inbound ports). It reports OS-level CPU/RAM/disk/network plus per-container stats, buffering to local SQLite while the network is down and draining on reconnect. |
| **Managed services** | One-click Postgres/Redis/RabbitMQ from templates. A `ServiceBinding` provisions the real database/user/password and injects them as env vars and secrets into the apps that consume them. |
| **Cloudflare DNS** | HTTP client against API v4. Automatic A/CNAME records when a custom domain is attached. Token encrypted in Settings and referenced by name. |
| **Notes and PinnedFacts** | Markdown and images per project/template/instance. PinnedFacts (IPs, credentials, commands) surface on the main card and are encrypted at rest. |
| **REST + MCP** | REST with OpenAPI 3.1 and dual auth: cookies for humans, scoped API keys for programs. MCP server at `https://aethra/mcp` with typed tools. |

---

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  VM-Central                                                    │
│  ┌──────────────┐   ┌──────────────────────────────────────┐   │
│  │ apps/web     │   │ apps/api                             │   │
│  │ Next.js 16   │◄──┤ ASP.NET Core (.NET 10)               │   │
│  │ App Router   │   │  • YARP (reverse proxy + TLS)        │   │
│  └──────────────┘   │  • SignalR Hub (satellite + UI)      │   │
│                     │  • MCP server (tools for agents)     │   │
│                     │  • Background workers                │   │
│                     │      Build, Deployment, Monitor,     │   │
│                     │      CertRenewal, OutboxDispatchers  │   │
│                     └──────────────────────────────────────┘   │
│                                                                │
│   ┌──────────────┐    ┌────────────────┐    ┌──────────────┐   │
│   │ PostgreSQL   │    │ Docker daemon  │    │ Internal     │   │
│   │ 12 schemas,  │    │ local builds   │    │ registry     │   │
│   │ 1 per module │    │ and services   │    │ (registry:2) │   │
│   └──────────────┘    └────────────────┘    └──────────────┘   │
└────────────────────────────▲───────────────────────────────────┘
                             │ SignalR (wss, egress only)
            ┌────────────────┴─────────┬─────────────────────┐
            │ VM-Satellite 1           │ VM-Satellite N      │
            │ apps/satellite (.NET)    │ ...                 │
            │ IContainerRuntime        │                     │
            │  ├─ DockerContainerRt    │                     │
            │  └─ PodmanContainerRt    │                     │
            │ OS + container metrics   │                     │
            └──────────────────────────┴─────────────────────┘
```

**A modular monolith with strict boundaries.** Each `Modules.<X>` is a bounded context with its own
PostgreSQL schema, its own DbContext, its own aggregates and its own local outbox. Cross-module
communication happens *only* through `IIntegrationEvent` in `Aethra.Shared.Contracts` — never by direct
reference. Violations are caught by `tests/Aethra.ArchitectureTests` using NetArchTest: code that crosses
a module boundary through the back door does not merge.

**Why SignalR instead of pull-based agents.** Beszel (WebSocket + CBOR) and Netdata (HTTP streaming
replication) — the two closest references — both use agent-initiated push over a persistent connection.
On Oracle Cloud and similar providers the satellites sit behind firewalls, and push only needs outbound
443. Bidirectionality comes for free: the central can send commands back down the same socket (build,
run, stream logs). SignalR is the native .NET equivalent, with automatic reconnect and backoff,
heartbeats and streaming.

---

## Stack

- **Backend** — .NET 10, ASP.NET Core, EF Core 10, YARP, SignalR, MediatR, FluentValidation, Polly,
  Docker.DotNet, Certes (ACME/Let's Encrypt), the `ModelContextProtocol` SDK.
- **Frontend** — Next.js 16 (App Router), TypeScript, Tailwind, `@microsoft/signalr`.
- **Database** — PostgreSQL 16, 12 schemas, one per bounded context (`projects`, `deployments`, `proxy`,
  `monitoring`, …).
- **Secrets at rest** — ASP.NET Data Protection with per-domain purposes (`aethra-integration-creds`,
  `aethra-webhook-secrets`, `aethra-cert-pfx`, `aethra-secrets-store`, …).
- **Tests** — NetArchTest (architectural fences), xUnit (handlers), Testcontainers (integration).

---

## Getting started

### With Docker

```bash
git clone https://github.com/Authoritt/Aethra.git
cd Aethra/deploy
cp .env.example .env        # set POSTGRES_PASSWORD and AETHRA_ADMIN_PASSWORD
docker compose up -d --build
```

Panel on <http://localhost:3000>, API on <http://localhost:5080>. The first build compiles the .NET and
Next images and is not fast.

Migrations are applied on boot because the compose sets `Aethra__ApplyMigrationsOnStart=true`. That is
opt-in on purpose: a managed deployment runs them from its pipeline instead, and you do not want two
instances migrating at once. `/openapi/v1.json` is served in `Development` only.

Compose brings up four containers: the central API, the Next.js panel, Postgres, and a local image
registry the build pipeline pushes to. The **satellite is deliberately not one of them** — it belongs
on each machine you want Aethra to manage, and you install it from the UI once the central is up.

Two things worth knowing before you point this at anything real:

- The API container mounts `/var/run/docker.sock`. That is what lets Aethra build and run your
  containers, and it is also root-equivalent access to the host. It is the same trade every
  Docker-based deploy tool makes, but you should make it knowingly.
- Compose refuses to start without both passwords set. There is no `changeme` fallback.

> **Honesty note:** this compose was wired on 2026-07-31. It is statically consistent with the
> Dockerfiles and with the configuration keys the code actually reads, but **it has not yet been run
> end to end on a clean machine.** If you are the first to try it, [an issue either way](../../issues)
> — it worked, or here is where it broke — is the single most useful thing you can send right now.

### Your first login

There is no sign-up page, and there should not be one on a box that can deploy to your
production. You do not create an account — the first one is created for you.

On first boot, if the users table is empty, Aethra seeds an admin from `AETHRA_ADMIN_EMAIL`
and `AETHRA_ADMIN_PASSWORD` in your `.env` and gives it the admin role. Log in with those.
That is also why compose refuses to start when either is missing: no default account, nothing
guessable.

After that, everyone else gets created from **Settings → Users**, with roles (admin,
developer, viewer), per-endpoint scopes and optional TOTP two-factor. No `curl` required.

If the users table is ever empty again, login falls back to validating against those same
environment variables and issues admin-equivalent claims, purely so the first session can
create real users. The moment one exists in the database, that fallback stops being used.

### From source

You will need **.NET 10 SDK**, **Node 24+**, and a **PostgreSQL 16** you can reach.

```bash
createdb -U postgres aethra

export Identity__AdminEmail="you@example.com"
export Identity__AdminPasswordSeed="a-strong-password"

dotnet run --project apps/api            # central, http://localhost:5000
cd apps/web && npm install && npm run dev # panel,  http://localhost:3000
dotnet run --project apps/satellite       # optional: a local satellite
```

If you do not set `Identity__*`, development falls back to `admin@aethra.local` / `aethra-dev`.
**Change that before exposing Aethra to anything.**

Coming from Coolify? See [`docs/migration-from-coolify.md`](docs/migration-from-coolify.md) and the
assisted scripts in `scripts/migrate-from-coolify.{sh,ps1}`.

---

## Repository layout

```
apps/
  api/         ASP.NET Core (central server)
  web/         Next.js 16 (UI)
  satellite/   Lightweight ASP.NET Core (per-VM agent)
src/
  Aethra.Shared.Kernel/         primitives: Result<T>, AethraId, IClock
  Aethra.Shared.Contracts/      integration events + cross-module interfaces
  Aethra.Shared.Infrastructure/ MediatR pipelines, outbox base, shared persistence
  Aethra.Modules.Projects/      Project, Template, Client, Instance, EnvVars, Secrets
  Aethra.Modules.Deployments/   Build, Deployment, Webhooks, orchestrators
  Aethra.Modules.Services/      ManagedService, ServiceBinding, provisioners
  Aethra.Modules.Proxy/         Routes (YARP), Certificates (ACME)
  Aethra.Modules.Vms/           Vm, SatelliteHub, registry
  Aethra.Modules.Metrics/       VmMetric, ContainerMetric
  Aethra.Modules.Monitoring/    Monitor, MonitorCheck
  Aethra.Modules.Cloudflare/    Zone, DnsRecord
  Aethra.Modules.Notes/         Note, PinnedFact, images
  Aethra.Modules.Identity/      User, ApiKey, scopes
  Aethra.Modules.Settings/      IntegrationCredential, BaseDomain, EnvironmentDefinition
  Aethra.Modules.Mcp/           MCP server + agent tools
tests/                          architecture fences, unit and integration tests
scripts/                        smoke tests, Coolify migration
deploy/                         docker-compose + Dockerfiles
docs/
```

---

## REST API and MCP

Full REST surface with OpenAPI 3.1 at `/openapi/v1.json`. The critical operations are also exposed as
MCP tools at `https://aethra/mcp`:

- `aethra_list_context` — aggregated snapshot (projects, VMs, services, domains).
- `aethra_create_template` / `aethra_create_client` / `aethra_create_instance`.
- `aethra_deploy_instance_native`, `aethra_list_deploys`, `aethra_get_deploy_logs`, `aethra_explain_failed_deploy`.
- `aethra_attach_domain`, `aethra_set_env_vars`, `aethra_list_secrets`.
- `aethra_bind_service` — provisions Postgres/Redis/RabbitMQ and injects the credentials.
- `aethra_query_metrics`, `aethra_get_monitor_status`, `aethra_add_note`.

Every response includes `next_actions: [{ tool, why, suggested_args }]` so the agent can propose the next
step without guessing the data model.

---

## Security

- Dual auth: HttpOnly cookie for the human UI, scoped API keys for agents and external clients.
- Every REST endpoint requires a `<resource>:<read|write>` scope. A cookie is equivalent to admin; an API
  key is limited to the scopes it declares.
- Sensitive endpoints (`/auth/me`, `/auth/logout`, API-key management, Settings integrations) are
  **cookie-only** — an API key cannot escalate into minting more keys or reading secrets.
- HMAC SHA-256 over the raw body on Git webhooks, compared with `CryptographicOperations.FixedTimeEquals`.
- Webhook secrets, integration credentials, PinnedFacts and service-binding passwords are encrypted at
  rest with Data Protection, using separate purposes so one compromise does not expose the rest.
- Automatic TLS via Let's Encrypt; the HTTP-01 challenge is served by YARP itself.
- Architecture tests keep the Domain free of EF/ASP.NET references and stop modules importing each
  other's internals.

Found a vulnerability? Please read [SECURITY.md](SECURITY.md) — do not open a public issue.

---

## Contributing

Contributions are welcome. [`CONTRIBUTING.md`](CONTRIBUTING.md) covers how to build it, how to run the
tests, and the two rules that keep this codebase alive: module boundaries and a pure domain layer.
By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Supporting the project

Aethra is built and maintained in the open. If it saves you a VPS, an afternoon, or a subscription,
sponsoring it keeps the work going — see the **Sponsor** button on GitHub, or
[`.github/FUNDING.yml`](.github/FUNDING.yml).

## License

[Apache License 2.0](LICENSE) — Copyright 2026 Authorit.
