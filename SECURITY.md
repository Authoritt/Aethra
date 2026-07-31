# Security Policy

Aethra holds deploy credentials, TLS private keys, webhook secrets, Cloudflare tokens and SSH keys for
the machines it manages. A vulnerability here is not theoretical — please treat it accordingly.

## Reporting a vulnerability

**Do not open a public issue.**

Use GitHub's private reporting: go to the [Security tab](https://github.com/Authoritt/Aethra/security)
→ **Report a vulnerability**. That opens a private channel visible only to the maintainers.

If that is not available to you, email **jhoan.1valencia@outlook.es** with `AETHRA SECURITY` in the
subject.

Please include:

- What you can do with it (read another tenant's secrets? escalate an API key? reach the Docker socket?).
- The steps to reproduce, ideally against a fresh local install.
- The commit or version you tested.
- Whether you have shared it with anyone else.

You will get an acknowledgement within **72 hours**. If a fix is going to take longer than that, you will
get a timeline rather than silence.

## What we consider a vulnerability

Aethra's threat model, in rough order of severity:

- **Tenant isolation** — one `Client` or `Instance` reading or writing another's variables, secrets,
  logs, metrics or containers.
- **Privilege escalation through an API key** — an API key obtaining anything beyond its declared scopes.
  Minting API keys and reading secrets are cookie-only *by design*; a way around that is a real finding.
- **Secrets at rest or in transit** — anything that exposes Data Protection payloads, integration
  credentials, PinnedFacts, service-binding passwords or certificate private keys.
- **Webhook forgery** — bypassing the HMAC SHA-256 signature check to trigger a build or deploy.
- **Reaching the host** — escaping into the Docker/Podman socket, the satellite's systemd unit, or the
  central process from a deployed workload.
- **Proxy poisoning** — making YARP route a hostname to a backend its owner did not authorize.

### Not vulnerabilities

- The development defaults (`admin@aethra.local` / `aethra-dev`, `Password=changeme` in the sample
  configs). They are documented as things to change before exposure. A report that they exist is not a
  finding; a way to *keep* them after a proper install is.
- Anything that requires the attacker to already hold a valid admin cookie.
- Missing hardening headers with no demonstrated impact.

## Disclosure

We will fix the issue, credit you in the release notes (unless you prefer otherwise), and publish an
advisory. Please give us a reasonable window to ship the fix before disclosing publicly — for anything in
the list above, 90 days is the default, and we will usually be much faster.

## Supported versions

Aethra has not cut a stable release yet. Until it does, **only `main` is supported** — security fixes land
there. This section will be replaced with a version table at 1.0.
