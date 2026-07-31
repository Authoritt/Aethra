# Contributing to Aethra

Thanks for being here. Aethra is a deploy platform people run in production, so the bar is
"does not surprise the operator at 3am" rather than "compiles". This document tells you how to build it,
what we will look at in review, and the two rules that keep the codebase from rotting.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

---

## Getting a working copy

You need **.NET 10 SDK**, **Node 24+**, and a **PostgreSQL 16** you can reach.

```bash
git clone https://github.com/Authoritt/Aethra.git
cd Aethra

createdb -U postgres aethra

export Identity__AdminEmail="you@example.com"
export Identity__AdminPasswordSeed="a-strong-password"

dotnet run --project apps/api          # central, http://localhost:5000
cd apps/web && npm install && npm run dev   # UI, http://localhost:3000
```

Migrations are applied on boot — you do not need a separate migration step to get started.

### Running the checks

```bash
dotnet build Aethra.slnx          # must be clean
dotnet test  Aethra.slnx          # architecture fences + unit + integration
```

Integration tests use Testcontainers, so they need a working Docker/Podman daemon. If you cannot run
them locally, say so in the PR — do not delete or skip them.

---

## The two rules

Most of what a reviewer will push back on comes down to these.

### 1. Modules do not touch each other

Every `src/Aethra.Modules.<X>` is a bounded context: its own PostgreSQL schema, its own `DbContext`, its
own aggregates, its own outbox. A module **never** references another module's types or reaches into its
tables. Cross-module communication happens through `IIntegrationEvent` in `Aethra.Shared.Contracts`.

This is not a style preference — `tests/Aethra.ArchitectureTests/ModuleIsolationTests.cs` fails the build
when you cross the line. If a fence is genuinely in your way, open an issue and argue for moving it.
Please do not weaken the test to make your PR green.

### 2. The domain layer stays pure

Domain code does not reference EF Core, ASP.NET, or any infrastructure. It knows nothing about how it is
persisted or served. `DomainPurityTests.cs` enforces this.

Practical consequences worth knowing before you start:

- Handlers return `Result<T>` (`Aethra.Shared.Kernel.Results`). Do not throw for expected failures —
  return `Error.Validation(...)`, `Error.NotFound(...)`, `Error.Conflict(...)`.
- Time comes from `IClock`, never `DateTime.UtcNow`. This is what makes the workers testable.
- Commands and queries are separate: query handlers must not mutate state.
- Validation lives in a FluentValidation validator next to the command, not scattered in the handler.

---

## What makes a good pull request

- **One thing.** A bug fix or one feature. Refactors that ride along make review much harder.
- **Explain the failure, not just the fix.** "Deployments to a satellite that reconnected mid-build were
  silently dropped because X" is worth ten lines of diff description.
- **Tests that would have failed before.** For a bug fix, the ideal PR includes a test that fails on
  `main` and passes with your change.
- **Anything touching secrets, auth, scopes, webhook signatures or the proxy gets read closely.** Expect
  questions. That is not distrust, it is the blast radius.
- **Say what you did not verify.** Honest gaps are fine and useful. Silent gaps are what break production.

Small fixes — typos, broken links, a confusing error message — do not need an issue first. Just send them.

### For larger changes, open an issue first

If you are planning something structural (a new module, a change to the deploy pipeline, a new external
integration), open an issue and let's agree on the shape before you spend a weekend on it. This is to
protect your time, not to gate you.

---

## Wanted contributions

Good places to start, roughly in order of how useful they would be:

- **`install.sh`** — a one-command installer that checks prerequisites, creates the database, applies
  migrations, seeds the admin password, writes a minimal `appsettings.Local.json` and starts both
  processes. The README currently points at it and it does not exist.
- **Tests for the MCP tools** — `src/Aethra.Modules.Mcp` has no covering tests today.
- **More managed-service templates** — see `src/Aethra.Modules.Services/Templates/` for the shape.
- **Documentation for a first deploy end to end**, written by someone who just did it for the first time
  and remembers where they got stuck.
- **Podman parity** — `IContainerRuntime` has a Podman implementation that gets less real-world use than
  the Docker one.

---

## Commits and branches

- Branch off `main`. Any branch name is fine.
- Write commit messages that say what changed and why. The first line is a summary; the body is for the
  reasoning that the diff cannot show.
- Rebasing or merging `main` before review — either is fine.

## Security issues

Do not open a public issue for a vulnerability. See [SECURITY.md](SECURITY.md).

## License

Aethra is [Apache-2.0](LICENSE). By contributing you agree that your contribution is licensed under the
same terms, per section 5 of the license.
