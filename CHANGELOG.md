# Changelog

All notable changes to this project are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until `1.0.0`, minor versions may contain breaking changes. Anything that requires a configuration
change on upgrade will be called out under **Breaking** with the exact steps.

## [0.1.0] — 2026-08-02

First tagged release. The code has been running in production on a single-operator deployment for
months; this is the first version that is pinnable, so you can install a known state instead of
tracking `main`.

### What it does

One control plane for a self-hosted box, where the four things share a database because in practice
they are not independent:

- **Deploy** — git push to build and run containers, blue-green swap, per-instance environment,
  scheduled jobs, preview environments, backups with retention.
- **Route** — reverse proxy (YARP) mapping hostname and path to containers, with automatic TLS
  issuance and renewal over ACME.
- **DNS** — Cloudflare zones, records and tunnels, managed from the same place as the routes that
  need them.
- **Watch** — uptime monitors with alerting, container and host metrics, notification channels.

When a deploy finishes, the monitor already knows which URL to watch. When a client is deleted, its
domains, certificates and metrics go with it.

### The part that is unusual

**The MCP server is the control plane, not a wrapper over the REST API.** 117 tools, so an agent can
deploy the current branch, explain why a container is down, add a DNS record or read the last failed
deploy's logs on its human's behalf, without anybody opening a dashboard. Mutations return
`next_actions` so the caller is not guessing at the sequence.

Authorization is scoped per tool. An API key **cannot mint another API key** — key management is
enforced at the route group, not by a check inside a handler, so that boundary cannot be forgotten.

### Added in this release

- Architecture fence asserting that all 117 MCP tools check their scope, so the invariant survives
  tool 118. Verified by removing a guard and confirming the check goes red.
- Tests for the scope authorization policies — the file that decides who may do what, previously
  untested. 112 cases, including the negative branches, verified by mutation.
- Every mutating tool without a simulation path now declares that in its description, so an agent
  reads it before calling rather than discovering it by performing the mutation.
- CI runs build and tests on push and nightly. The nightly run is not decoration: dependency audit
  failures can turn the build red with no commits.

### Fixed

- A proxy route's backend could be `file://` or a relative path on Linux, where
  `Uri.TryCreate(UriKind.Absolute)` accepts `/path`. Now restricted to http and https.
- Compose no longer falls back to a default password when one is unset; it refuses to start.

### Known limitations

Stated here rather than discovered later:

- **No replicas.** One service is one container; the proxy builds a single destination per cluster,
  and stale-container cleanup would remove a second instance. Horizontal scaling is not supported.
- **`dry_run` covers 28 of 74 mutating tools.** The remaining 46 declare that they cannot simulate.
  Tracked in [#7](https://github.com/Authoritt/Aethra/issues/7).
- **Scopes compose upward only.** You can grant a scope or grant `*`; "everything except X" is not
  expressible yet. Tracked in [#2](https://github.com/Authoritt/Aethra/issues/2), with a design
  contributed by @xpl0rer and @robauto-ai.
- **MCP sessions capture their principal once**, at session open, so a permission change takes effect
  on reconnect rather than immediately.
- **Every install so far has happened on the machine that wrote it.** First-run is likely broken in
  ways the author cannot see. Reports wanted in [#6](https://github.com/Authoritt/Aethra/issues/6).

### Requirements

Docker 24+, Compose v2.20+. Runs on arm64 and amd64. Postgres and a local registry come up with the
stack. See `deploy/.env.example` for the two passwords you must set.

[0.1.0]: https://github.com/Authoritt/Aethra/releases/tag/v0.1.0
